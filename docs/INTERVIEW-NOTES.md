# Technical Architecture Interview Notes

This document provides in-depth technical explanations and rationales for the architectural decisions implemented in this repository. It is structured around 16 common senior/principal systems architecture interview questions.

---

### 1. Why did you choose Vertical Slice Architecture over Clean Architecture or Onion Architecture?
**Answer:** Clean Architecture divides systems horizontally by technical role: Domain, Application, Infrastructure, and Presentation. In real-world enterprise service development, this structure causes high friction. Adding a simple property requires touching 4+ projects across 4+ layers. Code that changes together is stored as far apart as possible.

Vertical Slice Architecture organizes code by business capability. Each use case is an autonomous, self-contained slice containing its request, response, validator, handler, and endpoint in a single file. Slices talk directly to the `DbContext` without intermediate abstraction layers. This maximizes cohesion, minimizes coupling, and makes features easy to reason about, optimize, and delete without unintended blast radius.

---

### 2. Why did you choose native Minimal APIs over Controllers or third-party frameworks like FastEndpoints and Carter?
**Answer:** Minimal APIs in .NET 10 offer superior raw execution performance by bypassing the MVC model metadata, action invoker, and filter pipeline overhead. By implementing a lightweight `IEndpoint` interface with automated assembly discovery, each endpoint maps 1:1 with its vertical slice handler. 

I avoided third-party endpoint frameworks (FastEndpoints, Carter, Wolverine) because they introduce external dependencies, licensing risks, and proprietary abstractions over native ASP.NET Core primitives that can be implemented cleanly in 25 lines of C#.

---

### 3. Why inject `AppDbContext` directly into slice handlers instead of using Repository and Unit of Work patterns?
**Answer:** `DbContext` is already a Unit of Work, and `DbSet<T>` is already a Repository. Wrapping EF Core in generic repository interfaces (`IRepository<T>`) produces an impedance mismatch: it neuters LINQ query expressiveness, prevents selective column projections (`.Select()`), hides advanced EF Core 10 features like named query filters and batch operations, and encourages loading entire entity graphs into memory.

Direct `DbContext` access gives each slice complete control to write optimal, tailored LINQ queries. We test handlers against genuine PostgreSQL Testcontainers with Respawn, rendering repository mocks obsolete.

---

### 4. Why did you omit MediatR or an in-memory mediator library?
**Answer:** In-memory mediators were popularized to decouple controllers from business logic, but in a vertical slice Minimal API where an endpoint maps directly to a handler, an in-memory bus adds dynamic reflection overhead, obscures execution flow, breaks IDE navigation ("Go to Implementation"), and complicates debugging. 

Furthermore, MediatR 13+ switched to commercial licensing. Direct dependency injection of typed slice handlers is simpler, faster, compile-time safe, and free from external licensing baggage.

---

### 5. Why use explicit C# 14 record projections instead of AutoMapper or Mapster?
**Answer:** Reflection-based object mappers hide runtime failures until production, obscure SQL generation, and degrade throughput. With C# 14 and EF Core, writing explicit `.Select(t => new Response(...))` projections takes minimal effort, guarantees compile-time type safety, produces optimal SQL `SELECT` queries that fetch only required columns, and instantiates records at native memory speed with zero reflection overhead.

---

### 6. How does HybridCache differ from legacy `IDistributedCache`, and how does it prevent cache stampedes?
**Answer:** `IDistributedCache` acts solely as an L2 distributed cache, requiring network serialization on every call and lacking stampede protection. When a popular key expires, hundreds of concurrent requests query the database simultaneously.

.NET 10's `HybridCache` provides a unified two-tier topology: an in-process L1 memory cache and an out-of-process L2 Valkey cache. It natively implements **request coalescing**: if a key misses, only the first request executes the database query factory; all other concurrent callers await that single execution and share the returned value. It also natively supports multi-key tag-based invalidation.

---

### 7. Why choose sequential UUIDv7 (`Guid.CreateVersion7()`) over UUIDv4 or auto-incrementing integers?
**Answer:** Auto-incrementing integers leak business volume and allow easy ID enumeration attacks (IDOR/BOLA). Random UUIDv4 solves enumeration, but its random insertion order causes severe B-tree page splits and index fragmentation in relational databases.

UUIDv7 embeds a millisecond Unix timestamp in the most significant bits, guaranteeing monotonic ordering in PostgreSQL B-tree indexes while preserving the global uniqueness and unpredictability of UUIDs. It also allows generating valid entity IDs client-side or in-memory without database roundtrips.

---

### 8. How do EF Core 10 Named Query Filters work, and why are they superior to legacy global query filters?
**Answer:** Legacy EF Core global query filters applied a single predicate per entity. Disabling them required calling `.IgnoreQueryFilters()`, which indiscriminately stripped *all* filters on the entity—e.g., disabling soft-delete would also accidentally disable multi-tenant project isolation.

EF Core 10 introduces Named Query Filters, allowing multiple independent filters (e.g. `SoftDelete` and `Membership`) to be declared on an entity. Code can selectively ignore one filter (e.g. `IgnoreQueryFilter("SoftDelete")` in `RestoreTask`) while keeping multi-tenant isolation intact.

---

### 9. Why choose Keyset Cursor Pagination over Offset/Limit (`Skip`/`Take`)?
**Answer:** Offset pagination (`OFFSET 100000 LIMIT 20`) requires the database engine to scan and discard 100,000 rows, resulting in $O(N)$ performance decay. Additionally, if rows are inserted or deleted while a client is paging, rows shift across page windows ("pagination drift").

Keyset pagination uses indexed column comparisons (e.g. `(created_at, id) < (cursor_created_at, cursor_id)`), executing in $O(1)$ constant time regardless of page depth and guaranteeing that concurrent inserts/deletes never cause duplicate or skipped records.

---

### 10. How does native PostgreSQL full-text search compare to external engines like Elasticsearch?
**Answer:** External search clusters add high infrastructure cost, operational burden, and data synchronization lag (eventual consistency / dual-write problem).

PostgreSQL full-text search with stored generated `tsvector` columns, English stemming, and `GIN` indexes provides sub-millisecond search query execution within the primary ACID relational boundary. Using `EF.Functions.WebSearchToTsQuery` safely supports Google-like query syntax without search engine maintenance.

---

### 11. How do HTTP Weak ETags and optimistic concurrency prevent lost updates?
**Answer:** Pessimistic locking (`SELECT FOR UPDATE`) holds database locks across network boundaries, causing lock contention and deadlocks. 

We implement optimistic concurrency using an application-managed `row_version` integer incremented on every update. Read endpoints return a weak ETag header (`ETag: W/"1"`). If a client issues a conditional read with `If-None-Match`, the API returns `304 Not Modified`. On mutations, clients provide `If-Match`. If the database row version differs from the header, the update throws `DbUpdateConcurrencyException`, returning RFC 9457 `412 Precondition Failed` or `409 Conflict`.

---

### 12. How does the distributed idempotency filter handle concurrent requests in flight versus completed retries?
**Answer:** Slices use an `IdempotencyFilter` backed by Valkey. When a request arrives with an `Idempotency-Key` header, the request body is buffered to compute a SHA-256 hash. 

An atomic `SETNX` lock is acquired in Valkey:
- If the key exists with state `in_progress`, the filter terminates and returns **`409 Conflict`**.
- If the key exists with state `completed`, the filter compares SHA-256 payload hashes: if identical, it replays the cached HTTP response with `Idempotent-Replayed: true`; if mismatched, it returns **`422 Unprocessable Content`**.
- If new, the handler executes, and on success, the response and payload hash are cached with a 24-hour expiration.

---

### 13. How does your authorization architecture mitigate Broken Object Level Authorization (OWASP BOLA)?
**Answer:** BOLA (API 1:2023) occurs when an authenticated user manipulates IDs to access unauthorized resources. We mitigate this using a two-tier defense-in-depth model:
1. **Perimeter:** ASP.NET Core policies enforce OAuth scopes (`tasks.read`, `tasks.write`).
2. **Object Level:** Slice handlers call `IProjectAccessService.EnsureAccessAsync(...)` verifying database membership and minimum roles (`Viewer`, `Editor`, `Owner`).
3. **Information Concealment:** If a user lacks membership in the target project, the API returns **`404 Not Found`** rather than `403 Forbidden`, preventing attackers from probing project existence.
4. **Database Defense:** EF Core named query filters ensure multi-tenant boundaries hold even if handler logic is bypassed.

---

### 14. Why use Ubuntu Noble Chiseled container images, and how do you execute container health checks without curl?
**Answer:** Standard images contain shells (`/bin/sh`), package managers (`apt`), and standard utilities, creating massive CVE attack surfaces. Chiseled Ubuntu contains *only* the .NET runtime and standard C libraries, running as an unprivileged user (UID 1654).

Because chiseled images contain no curl or shell, we invoke the compiled application directly using built-in CLI switches:
```dockerfile
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["dotnet", "TaskTracker.Api.dll", "--healthcheck"]
```

---

### 15. How do you design OpenTelemetry instrumentation to avoid vendor lock-in?
**Answer:** We instrument exclusively against native .NET `System.Diagnostics.ActivitySource` (tracing) and `System.Diagnostics.Metrics.Meter` (metrics) APIs without taking dependencies on third-party APM vendor SDKs (Datadog, New Relic). 

Telemetry is exported via standard OpenTelemetry Protocol (OTLP). Switching observability backends requires changing only the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable.

---

### 16. Why must database migrations run in an independent CLI init container rather than at application startup?
**Answer:** Executing `Database.Migrate()` on startup causes race conditions and deadlocks when multiple API replicas boot concurrently. It also forces web pods to run under an over-privileged database account with full DDL rights.

We decouple migrations into a standalone CLI switch (`dotnet TaskTracker.Api.dll --migrate`) run as an init container or one-shot job (`migrate` service in `compose.yaml`). Web replicas boot instantaneously, and production web connection strings require only DML permissions.

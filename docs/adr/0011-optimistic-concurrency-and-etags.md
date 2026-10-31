# 11. Optimistic Concurrency and HTTP Weak ETags

Date: 2026-10-31
Status: Accepted

## Context

In collaborative task management systems, concurrent updates to the same entity (e.g. two users editing the same task description, or one user completing a task while another edits its labels) cause "lost updates" if unmanaged.

Pessimistic locking (`SELECT FOR UPDATE`) holds database locks across network roundtrips, causing lock contention, deadlocks, and severe latency spikes. Conversely, unconstrained updates overwrite concurrent changes silently.

HTTP provides standardized mechanisms for optimistic concurrency: conditional requests via `ETag`, `If-Match`, and `If-None-Match` (RFC 9110).

## Decision

I chose **application-managed row versions with HTTP Weak ETags and `If-Match` headers** for all mutable entities (`Project`, `TaskItem`).

1. Each mutable entity includes an unsigned integer `RowVersion` column (`uint row_version`) configured in EF Core as a concurrency token:
   ```csharp
   builder.Property(t => t.RowVersion)
       .HasColumnName("row_version")
       .IsConcurrencyToken();
   ```
2. On `SaveChanges`, `AuditTimestampInterceptor` automatically increments `RowVersion` for modified entities.
3. Read endpoints (`GetProject`, `GetTask`) emit a weak ETag header derived from the row version:
   ```
   ETag: W/"1"
   Cache-Control: private, no-cache
   ```
4. If a client provides `If-None-Match: W/"1"`, the server immediately returns `304 Not Modified` with an empty body, saving serialization and bandwidth.
5. Mutation endpoints (`UpdateProject`, `UpdateTask`) require the `If-Match` header. If the row version in the database differs from the header, the update throws `DbUpdateConcurrencyException`, translated into RFC 9457 `412 Precondition Failed` or `409 Conflict`.

## Consequences

### Positive
- **Standardized Web Semantics:** Adheres strictly to RFC 9110 conditional request standards.
- **Lost Update Prevention:** Concurrent writers cannot silently overwrite each other's changes.
- **Bandwidth Efficiency:** 304 Not Modified responses eliminate payload transfer when data has not changed.
- **Zero Database Lock Contention:** Eliminates pessimistic table and row locking.

### Negative
- Clients must capture ETags and supply `If-Match` headers on mutations.

### Alternatives Considered
- **PostgreSQL `xmin` System Column:** Initially evaluated, but EF Core's explicit management of PostgreSQL system columns can produce dialect-specific migration friction. An application-managed column is portable and explicitly auditable.
- **Pessimistic Locking (`SELECT FOR UPDATE`):** Rejected due to throughput bottlenecks and deadlock risks under load.

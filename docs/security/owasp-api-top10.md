# OWASP API Security Top 10 (2023) Mitigations

This document outlines how the Task Tracker API systematically addresses every risk in the OWASP API Security Top 10 (2023).

---

## 1. API1:2023 - Broken Object Level Authorization (BOLA)
- **Threat:** Callers manipulate resource IDs in URLs (e.g. `/api/v1/tasks/{taskId}`) to read or modify resources belonging to other tenants.
- **Mitigation:**
  - Slices evaluate project membership via `IProjectAccessService.EnsureAccessAsync(...)` requiring specific roles (`Viewer`, `Editor`, `Owner`).
  - Slices return **`404 Not Found`** instead of `403 Forbidden` when membership is lacking, deliberately preventing resource existence probing.
  - EF Core named query filters (`SoftDelete`, `Membership`) enforce tenant boundaries at the database query generation level as a defense-in-depth guarantee.

## 2. API2:2023 - Broken Authentication
- **Threat:** Flawed token issuance, weak signature validation, or missing credential expiration.
- **Mitigation:**
  - Standard JWT Bearer authentication requiring explicit issuers and audiences.
  - In local development, the token issuer (`POST /dev/token`) is strictly guarded:
    - Disabled by default in Production environments (`DevIssuer__Enabled=false`).
    - Guarded by a strict sliding-window rate limit (10 requests/minute).
    - Keys are generated locally into git-ignored files.

## 3. API3:2023 - Broken Object Property Level Authorization (Mass Assignment)
- **Threat:** Attackers send unexpected JSON fields (e.g. `isAdmin: true`, `deletedAt: null`, `rowVersion: 999`) to overwrite sensitive properties.
- **Mitigation:**
  - Strict JSON deserialization (`StrictJsonMiddleware`) rejects unknown properties.
  - Request contracts are immutable, dedicated C# records containing only fields valid for that specific operation.
  - Slices project explicitly into domain entities; no automated reflection-based mapping tools (AutoMapper) are permitted.

## 4. API4:2023 - Unrestricted Resource Consumption
- **Threat:** Denial of service via payload bloat, infinite queries, or unbounded page sizes.
- **Mitigation:**
  - `RequestHardeningMiddleware` limits request body sizes to 1 MB.
  - Request timeout middleware enforces a 30-second execution deadline.
  - Keyset pagination enforces strict `limit` bounds (max 100 items per request).
  - Multi-tier sliding window rate limiters partition traffic by authenticated user ID and client IP.

## 5. API5:2023 - Broken Function Level Authorization
- **Threat:** Standard users accessing administrative endpoints (e.g. task restoration or member role promotion).
- **Mitigation:**
  - Endpoints enforce granular OAuth scopes (`tasks.admin`, `tasks.write`) via ASP.NET Core authorization policies.
  - Administrative slices require the `Owner` project role.
  - Last-owner protection rules prevent project owners from demoting themselves or leaving a project without an active owner.

## 6. API6:2023 - Unrestricted Access to Sensitive Business Flows
- **Threat:** Automated bot scripts spamming task creation, inflating comment volumes, or replay attacks.
- **Mitigation:**
  - `IdempotencyFilter` requires or verifies `Idempotency-Key` headers on mutating requests, backed by atomic distributed Valkey locks.
  - Sliding-window rate limiters throttle mutation endpoints to 120 req/min per user.

## 7. API7:2023 - Server-Side Request Forgery (SSRF)
- **Threat:** Exploiting webhooks or file import URLs to probe internal network infrastructure.
- **Mitigation:**
  - By architectural discipline, webhooks and external URL fetching are non-goals. The API has zero outbound network capabilities.

## 8. API8:2023 - Security Misconfiguration
- **Threat:** Verbose stack traces, unhardened containers, insecure TLS, or missing security headers.
- **Mitigation:**
  - Multi-stage Docker build using Ubuntu Noble Chiseled images (no shell, no package manager, non-root user UID 1654).
  - `.NET Diagnostics` disabled (`DOTNET_EnableDiagnostics=0`).
  - Security headers middleware injects `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'`, and `Referrer-Policy: no-referrer`.
  - Errors format as RFC 9457 Problem Details with zero internal exception leakages.

## 9. API9:2023 - Improper Inventory Management
- **Threat:** Outdated API versions (v0, v1-beta) left exposed to attackers without authentication.
- **Mitigation:**
  - All endpoints are registered into an explicit, versioned route group (`/api/v1`).
  - Architecture tests guarantee that every `IEndpoint` implementer is mapped and versioned.
  - Default fallback policy requires authentication for all unannotated endpoints.
  - OpenAPI schema drift tests (`OpenApiDriftTests`) ensure API specifications stay version-controlled.

## 10. API10:2023 - Unsafe Consumption of APIs
- **Threat:** Blindly trusting third-party API payloads.
- **Mitigation:**
  - The API does not consume external third-party HTTP services.

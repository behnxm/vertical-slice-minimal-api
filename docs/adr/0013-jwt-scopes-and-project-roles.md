# 13. Dual-Layer Authorization: OAuth Scopes and Project-Level Roles

Date: 2026-10-31
Status: Accepted

## Context

Authorization in multi-tenant SaaS applications often suffers from one of two failure modes:
1. **Coarse-grained role explosion:** Global roles (e.g. `Admin`, `User`) are packed into JWT tokens. This breaks down when a user is an `Owner` in Project A, an `Editor` in Project B, and a `Viewer` in Project C, or when access tokens grow unbounded.
2. **Missing scope enforcement:** Services check who the user is, but fail to check what permissions the client application was granted (e.g., read-only tokens performing write actions).

## Decision

I chose a **two-tier, defense-in-depth authorization model**:

### Tier 1: JWT OAuth Scopes (Client Capability)
- Evaluated at the HTTP boundary via ASP.NET Core authorization policies:
  - `tasks.read`: Required for `GET` endpoints.
  - `tasks.write`: Required for mutating endpoints (`POST`, `PUT`, `DELETE`).
  - `tasks.admin`: Required for administrative operations (e.g., restoring soft-deleted tasks).
- Rejects unauthorized requests at the perimeter before any handler logic or database queries execute.

### Tier 2: Project-Level Roles (Object-Level Access Control / OWASP BOLA Mitigation)
- Evaluated within slice handlers via `IProjectAccessService`:
  - Roles: `Viewer`, `Editor`, `Owner`.
  - Stored in the relational database (`project_members` table) and cached via HybridCache.
  - Slices explicitly declare the minimum role required:
    - Reading project resources requires `Viewer`.
    - Creating/updating tasks and comments requires `Editor`.
    - Modifying project settings, archiving, or managing memberships requires `Owner`.
- If a user lacks membership in the project, the system returns **`404 Not Found`** rather than `403 Forbidden`, deliberately preventing attackers from probing or enumerating project existence.

## Consequences

### Positive
- Strict mitigation of Broken Object Level Authorization (OWASP API 1:2023).
- Clean separation between client application grants (OAuth scopes) and tenant user permissions (project roles).
- High performance: membership checks are cached in HybridCache, avoiding redundant database lookups.

### Negative
- Handlers must explicitly call `EnsureAccessAsync(...)` or let EF Core named query filters enforce boundaries.

### Alternatives Considered
- **Claims-based project roles in JWT:** Packing all project memberships into the JWT token causes token bloat and cannot reflect immediate role revocations without token reissue.

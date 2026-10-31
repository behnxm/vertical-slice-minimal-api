# 8. EF Core 10 Named Query Filters for Defense-in-Depth

Date: 2026-10-31
Status: Accepted

## Context

Multi-tenant security and soft deletion are critical concerns in enterprise APIs. If developers must remember to append `.Where(t => !t.IsDeleted && t.Project.Members.Any(...))` to every query across dozens of slices, eventual omission is guaranteed, resulting in critical data leaks (BOLA/IDOR).

EF Core has long supported Global Query Filters (`HasQueryFilter`), but historically they suffered from severe limitations:
- A filter could only be configured once per entity type.
- Disabling a filter required calling `.IgnoreQueryFilters()`, which indiscriminately stripped *all* filters on the entity (e.g. disabling soft-delete would also accidentally disable tenant/membership isolation).

EF Core 10 introduces **Named Query Filters**, allowing independent filters to be declared and selectively ignored.

## Decision

I chose **EF Core 10 Named Query Filters** as an architectural defense-in-depth layer.

1. **`SoftDelete` filter:** Configured on `TaskItem`, automatically filtering `!IsDeleted`. Ignored solely in administrative restore operations (`RestoreTask`) and background purge jobs (`SoftDeletePurgeService`).
2. **`Membership` filter:** Configured across tenant entities, ensuring users only query records within projects where they hold verified membership.
3. Architecture tests police `.IgnoreQueryFilters()`: any invocation outside an explicit, justified allowlist triggers a test failure.

## Consequences

### Positive
- **Guaranteed Isolation:** Slices cannot accidentally leak soft-deleted records or records belonging to foreign projects.
- **Granular Control:** Operations requiring bypass (e.g. `RestoreTask`) can explicitly disable `IgnoreQueryFilter("SoftDelete")` without disabling multi-tenant boundaries.
- **Auditable Safety:** Enforced automatically by architecture tests.

### Negative
- Query performance must be monitored to ensure the added filter predicates match composite indexes.

### Alternatives Considered
- **Manual LINQ Predicates in Handlers:** Rejected as prone to developer oversight and security vulnerabilities.
- **Database Row-Level Security (PostgreSQL RLS):** Powerful, but introduces complex session variable plumbing (`SET LOCAL role...`) between connection pool reuses.

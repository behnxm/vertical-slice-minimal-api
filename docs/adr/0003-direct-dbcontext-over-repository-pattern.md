# 3. Direct DbContext Over Repository Pattern

Date: 2026-10-31
Status: Accepted

## Context

The Repository and Unit of Work patterns were introduced to decouple business logic from data access mechanisms and to allow mock-based unit testing. However, Entity Framework Core already implements both patterns natively: `DbSet<T>` is a Repository, and `DbContext` is a Unit of Work.

Wrapping EF Core in custom repository interfaces (e.g., `IRepository<TaskItem>`, `IGenericRepository<T>`) produces severe architectural liabilities:
- It strips away the power of IQueryable, forcing either leaky abstractions (`repo.Query()`) or explosion of bespoke repository methods (`repo.GetTasksWithMembersAndCommentsByProjectIdFiltered(...)`).
- It hinders optimized LINQ projections (`.Select()`), leading developers to fetch full entities into memory before mapping.
- It obstructs advanced EF Core features like named query filters, batch operations (`ExecuteUpdateAsync`, `ExecuteDeleteAsync`), and keyset pagination.
- It promotes fragile mock-based testing instead of testing against real databases.

## Decision

I chose **direct `DbContext` consumption in slice handlers**, completely omitting custom repositories and units of work.

1. Each slice handler injects `AppDbContext` directly.
2. Read operations execute tailored LINQ queries with `.AsNoTracking()` and explicit projections into response records.
3. Write operations load aggregate roots, invoke domain methods to enforce invariants, and call `await db.SaveChangesAsync()`.
4. Testing is performed against a real PostgreSQL instance via Testcontainers and Respawn, eliminating any need to mock database interfaces.

## Consequences

### Positive
- Zero wrapper ceremony; LINQ queries are completely visible and optimizable inside the slice where they are used.
- Full access to native PostgreSQL and EF Core 10 features (e.g. `EF.Functions.WebSearchToTsQuery`, array containment `@>`, named query filters).
- High execution performance through direct projections into immutable response records.

### Negative
- Handlers are coupled to EF Core. (In practice, switching ORMs on an established enterprise project is exceptionally rare, and replacing a generic repository wrapper does not simplify an ORM migration anyway).

### Alternatives Considered
- **Generic Repository (`IRepository<T>`):** Rejected as an anti-pattern that creates an impedance mismatch with EF Core.
- **Specification Pattern:** Adds an unnecessary layer of indirection for simple and moderate queries; LINQ is already a standardized specification language in C#.
- **Dapper:** Considered for read queries, but EF Core 10 query generation in .NET 10 is fast and type-safe.

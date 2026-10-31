# 5. Explicit C# 14 Projections Over Object Mapping Libraries

Date: 2026-10-31
Status: Accepted

## Context

Object-to-object mapping libraries like AutoMapper or Mapster are commonly introduced to eliminate boilerplate code when converting between domain entities and DTOs. However, in enterprise systems, mapping libraries are a frequent source of production bugs and performance degradation:
- Reflection-based mapping hides runtime errors that compiler type-checking would catch instantly.
- Complex mapping profiles with conditionality (`opt.MapFrom(...)`) are notoriously hard to test, debug, and trace.
- When used with ORMs, mapping configurations often fail to translate cleanly to SQL expressions (`ProjectTo`), triggering N+1 query execution or silent client-side evaluation.
- AutoMapper is legacy software with deprecation warnings and performance overhead.

## Decision

I chose **explicit, strongly-typed C# 14 projections and factory methods** over any mapping library.

1. Responses are constructed using explicit record instantiations or static projection methods:
   ```csharp
   var tasks = await query
       .Select(t => new Response(
           t.Id,
           t.ProjectId,
           t.Title,
           t.Status,
           t.Priority,
           t.AssigneeId,
           t.Labels,
           t.CreatedAtUtc,
           t.UpdatedAtUtc))
       .ToListAsync(ct);
   ```
2. For entity-to-response projections outside LINQ expressions, entities or responses expose explicit mapping helpers or constructors.
3. Architecture tests forbid any reference to AutoMapper or Mapster.

## Consequences

### Positive
- **100% Compile-Time Safety:** Renaming a property or altering a type immediately produces compiler errors across all affected slices.
- **Optimal SQL Generation:** EF Core translates explicit LINQ `.Select()` projections into targeted SQL `SELECT col1, col2` queries, never fetching unneeded columns.
- **Zero Reflection Overhead:** C# records instantiate at native memory speed with zero dynamic reflection or expression compilation.
- **Clarity:** Any engineer reading the slice immediately sees every field mapped without hunting for global configuration profiles.

### Negative
- Minor boilerplate when mapping records with many properties. (In practice, vertical slice responses are tailored to the use case and rarely contain more than 8-12 fields).

### Alternatives Considered
- **AutoMapper:** Rejected due to runtime reflection hazards, maintenance history, and anti-patterns.
- **Mapster:** Fast, but still introduces a runtime dependency and obscures explicit LINQ translations.
- **Riok.Mapperly (Source Generator):** Viable, but explicit C# 14 record construction was clean and required no external analyzers or source generators.

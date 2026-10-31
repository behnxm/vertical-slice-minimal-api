# 4. In-Process Vertical Slice Invocation Without a Mediator Library

Date: 2026-10-31
Status: Accepted

## Context

MediatR has long been the default mechanism in the .NET ecosystem for decoupling controllers from handlers. However, using an in-memory mediator for intra-process dispatch carries notable drawbacks:
- It hides the execution flow behind runtime reflection and service provider lookups, breaking IDE "Go to Implementation" and static analysis.
- It introduces licensing volatility (MediatR 13+ moved to commercial licensing).
- Developers often misuse mediator pipeline behaviors as an ad-hoc middleware layer, obscuring where cross-cutting logic executes.
- In a web API where each HTTP endpoint maps 1:1 with a handler, an in-memory bus adds overhead without providing genuine decoupling.

## Decision

I chose **direct, typed handler invocation** without MediatR or any mediator library.

1. Each slice encapsulates its `Handler` as a nested class:
   ```csharp
   public static class CreateTask
   {
       public sealed record Request(...);
       public sealed record Response(...);
       public sealed class Handler(AppDbContext db, ICurrentUser currentUser)
       {
           public async Task<Result<Response>> HandleAsync(Request request, CancellationToken ct) { ... }
       }
   }
   ```
2. The nested `Endpoint` class injects `Handler` directly:
   ```csharp
   group.MapPost("/", async (Request req, Handler handler, CancellationToken ct) =>
   {
       var result = await handler.HandleAsync(req, ct);
       ...
   });
   ```
3. Handlers and validators are registered automatically via standard Microsoft DI assembly scanning at startup.
4. Cross-cutting concerns are addressed via native ASP.NET Core endpoint filters, EF Core interceptors, and middleware.

## Consequences

### Positive
- Direct method invocation: zero reflection at dispatch time, full stack-trace clarity, and instant IDE navigation.
- Zero external package dependency or commercial licensing exposure.
- Explicit pipeline: cross-cutting filters are applied deliberately using ASP.NET Core's idiomatic endpoint filter pipeline.

### Negative
- Handlers cannot easily be intercepted by a single generic mediator pipeline behavior (instead, endpoint filters or custom delegates are used).

### Alternatives Considered
- **MediatR:** Rejected due to commercial licensing and unnecessary abstraction overhead.
- **Custom In-Memory Bus:** Rejected as reinventing MediatR when direct DI injection is simpler, faster, and more maintainable.

# 2. Minimal APIs Over Controllers and Endpoint Frameworks

Date: 2026-10-31
Status: Accepted

## Context

ASP.NET Core offers traditional Controller-based routing as well as modern Minimal APIs introduced in .NET 6 and expanded in .NET 8, 9, and 10. Additionally, several third-party libraries exist to impose structure on Minimal APIs (such as FastEndpoints, Carter, and Wolverine.HTTP).

Traditional controllers bundle disparate actions into a single class, violating single-responsibility principles and leading to massive constructor injection lists ("God controllers"). Conversely, third-party endpoint frameworks introduce external package dependencies and proprietary abstractions that may suffer from license changes, abandoned maintenance, or breaking updates.

## Decision

I chose native **ASP.NET Core Minimal APIs** with an explicit `IEndpoint` contract and automatic assembly scanning at startup.

1. Each slice defines an `Endpoint` class implementing `IEndpoint`:
   ```csharp
   public interface IEndpoint
   {
       void MapEndpoint(IEndpointRouteBuilder app);
   }
   ```
2. Endpoints are registered into a versioned prefix group (`/api/v1`) that enforces default authentication.
3. Native ASP.NET Core endpoint filters handle validation, idempotency, and audit telemetry.
4. I rejected external endpoint wrappers (FastEndpoints, Carter, Wolverine).

## Consequences

### Positive
- Zero external dependencies for routing; purely native .NET 10 APIs.
- Extreme performance: Minimal APIs bypass the MVC model metadata, filter pipeline, and action invoker overhead.
- Clean separation: Each endpoint maps exactly one HTTP route and delegates directly to its nested slice handler.
- Full compatibility with OpenAPI and ASP.NET Core's native OpenAPI generator.

### Negative
- Requires a small custom scanner (~25 lines in `EndpointRegistrationExtensions.cs`) to discover and invoke `IEndpoint` implementations.
- Developers familiar only with MVC controllers must learn the endpoint routing DSL.

### Alternatives Considered
- **ASP.NET Core MVC Controllers:** Rejected due to bloated constructor injection, controller class size, and runtime overhead.
- **FastEndpoints:** Powerful, but introduces a heavy third-party framework abstraction over native ASP.NET Core primitives.
- **Carter:** Lightweight, but does not provide anything that cannot be achieved with 25 lines of native LINQ reflection.

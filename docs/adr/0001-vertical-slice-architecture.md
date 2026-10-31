# 1. Vertical Slice Architecture

Date: 2026-10-31
Status: Accepted

## Context

When structuring ASP.NET Core web applications, teams frequently gravitate toward Clean Architecture, Onion Architecture, or Hexagonal Architecture. These patterns divide a codebase horizontally into rigid layers: Domain, Application, Infrastructure, and Presentation. 

In practice, for standard web APIs and service systems, horizontal layering introduces high friction:
- Every change touches all four layers, requiring edits across four or more projects just to add a single field to an endpoint.
- Code that changes together is placed as far apart as possible.
- Abstractions proliferate (Use Cases, Queries, Commands, DTOs, Mapping profiles, Domain Services, Repositories, Interfaces) to satisfy layering rules rather than business requirements.
- The cognitive load of navigating the codebase is dominated by ceremony.

## Decision

I chose **Vertical Slice Architecture** for this service. Instead of technical layers, the codebase is partitioned along business capabilities (features and use cases).

1. Each slice is an autonomous, self-contained unit located in `Features/<Feature>/<UseCase>.cs`.
2. A slice defines its own `Request`, `Response`, `Validator`, `Handler`, and `Endpoint` in one place.
3. Handlers interact directly with the database via `AppDbContext`, eliminating abstraction layers that exist solely for ceremony.
4. Slices are isolated from one another. Slice A may never reference or depend on Slice B.
5. Common code is minimized and governed by the "Rule of Three": code is only extracted to `Common` or `Domain` after being independently required across three distinct slices.
6. Boundaries are enforced through architecture tests (`NetArchTest.eXpect`).

## Consequences

### Positive
- **High Cohesion:** Everything required to understand, debug, modify, or delete a feature lives in a single file or directory.
- **Low Coupling:** Changes to one use case cannot break another use case because they share no slice-specific state or abstractions.
- **Fast Onboarding:** An engineer can understand a feature by reading one file top-to-bottom without jumping across four projects.
- **Tailored Optimization:** Read slices can use raw projections (`AsNoTracking()`, custom SQL, or keyset pagination) without being constrained by a generic repository designed for writes.

### Negative
- Potential for code duplication across slices (e.g., similar response DTO shapes or query projections).
- Requires discipline to prevent slices from growing unbounded or referencing each other.

### Alternatives Considered
- **Clean / Onion Architecture:** Rejected due to excessive layer ceremony and unnecessary indirection for a focused web API.
- **Modular Monolith:** Suitable for large systems with multiple domain boundaries, but excessive for a cohesive single-boundary service like this Task Tracker.

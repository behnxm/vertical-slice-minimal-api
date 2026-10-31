# Beginner's Guide: Understanding Vertical Slice Architecture

If you are new to Vertical Slice Architecture or transitioning from traditional Clean/Layered Architecture, this guide explains what this architecture is, where execution starts, how the folders are organized, and why this design solves the real-world maintenance headaches of modern web services.

---

## 1. What is the Entry Point? Where Does the Program Start?

If you come from a Clean Architecture or MVC background, you might wonder:
> *"In Clean Architecture, I have a WebApi project that acts as the entry point and references Application, Infrastructure, etc. What is the startup entry point here?"*

The answer is simple: **It is still a standard ASP.NET Core Web API with `Program.cs`!**

Vertical Slice Architecture is not an esoteric framework or an unusual executable format—it is an **organizational and design philosophy** for how code is structured *inside* the project.

In this repository, the main entry point is:
[`src/TaskTracker.Api/Program.cs`](file:///d:/Projects/Code/ant/VerticalSliceMinimalApi/src/TaskTracker.Api/Program.cs)

When you execute:
```bash
dotnet run --project src/TaskTracker.Api
```

Here is what happens in `Program.cs` from top to bottom:
1. **CLI Commands Check:** The application checks if you passed command-line flags (like `--migrate`, `--seed-demo`, or `--healthcheck`). If so, it executes that specific operation and exits cleanly.
2. **Service Registration (DI):** It registers cross-cutting services into the `WebApplicationBuilder`:
   - Database connection (`AppDbContext` with PostgreSQL).
   - Caching (`HybridCache` with Valkey distributed L2).
   - Authentication (JWT Bearer token validation) and Authorization policies (`tasks.read`, `tasks.write`, `tasks.admin`).
   - Sliding-window rate limiting policies.
   - OpenAPI and Scalar documentation generators.
   - FluentValidation validators and slice handlers via a single assembly scan.
3. **HTTP Middleware Pipeline:** Configures the request pipeline (Security headers, exception handling, strict JSON parsing, authentication, authorization, and rate limiting).
4. **Endpoint Discovery:** It scans the assembly for all classes implementing `IEndpoint` and maps them into the `/api/v1` versioned route group.
5. **Host Run:** It calls `app.Run()` to start listening for HTTP traffic on port 8080.

---

## 2. How Does This Differ from a "Normal" Project?

To understand Vertical Slice Architecture, let's contrast three common ways people structure .NET web backends:

### A. The "Junior / Beginner" Project (Fat Controllers & Anemic Models)
- **Structure:** A single project with a `Controllers/` folder and a `Models/` folder.
- **The Problem:** 
  - Controllers quickly become 2,000-line "God classes" containing database queries, business rules, input validation, and HTTP mapping mixed together.
  - Zero encapsulation: any action can mutate any database table directly with no domain invariants.
  - No separation of concerns, impossible to unit test, and prone to breaking unexpectedly when multiple developers edit the same giant controller.

### B. The Traditional "Clean / Onion" Architecture (Horizontal Layers)
- **Structure:** Divided into 4 or 5 separate `.csproj` projects:
  - `Domain/` (Entities, value objects)
  - `Application/` (Use cases, Commands, Queries, MediatR handlers, DTOs, AutoMapper profiles, Repository interfaces)
  - `Infrastructure/` (EF Core DbContext, Repository implementations, external services)
  - `WebApi/` (Controllers, Program.cs)
- **The Problem:** 
  - **Horizontal Coupling:** Adding a single field (e.g. adding `dueDate` to a task) requires editing **6 to 8 separate files across 4 distinct projects** (`TaskController.cs` $\rightarrow$ `CreateTaskCommand.cs` $\rightarrow$ `CreateTaskCommandHandler.cs` $\rightarrow$ `ITaskRepository.cs` $\rightarrow$ `TaskRepository.cs` $\rightarrow$ `TaskDto.cs` $\rightarrow$ `TaskMappingProfile.cs`).
  - **Ceremony over Substance:** 80% of the code written is pure indirection (pass-through interfaces, mapping boilerplate, command wrappers) rather than business logic.
  - **Impedance Mismatch:** Wrapping EF Core in generic repositories strips away the power of LINQ projections, named query filters, and batch updates.

### C. Vertical Slice Architecture (This Project)
- **The Core Idea:** **The feature is the unit of change, not the technical layer.**
- Code that changes together lives together.
- Adding or modifying a use case touches **exactly one file**.
- Handlers talk directly to `AppDbContext` using tailored LINQ projections—no generic repository middleman.
- No in-memory mediator library (MediatR)—handlers are typed classes injected directly into endpoints.
- No AutoMapper—explicit C# 14 record projections are compile-time safe and produce optimal SQL.
- Strict boundaries are policed by automated **Architecture Tests** (`NetArchTest.eXpect`) so slices never reference each other.

---

## 3. Directory & Folder Structure Walkthrough

Here is the architectural layout inside `src/TaskTracker.Api/`:

```
src/TaskTracker.Api/
├── Features/          <-- THE HEART OF THE APPLICATION (Vertical Slices)
│   ├── Projects/      <-- All use cases related to projects
│   │   ├── CreateProject.cs
│   │   ├── ListProjects.cs
│   │   ├── GetProject.cs
│   │   ├── UpdateProject.cs
│   │   ├── ArchiveProject.cs
│   │   ├── UnarchiveProject.cs
│   │   ├── GetProjectSummary.cs
│   │   └── GetMe.cs
│   ├── Members/       <-- Member management (AddMember, ListMembers, UpdateRole, RemoveMember)
│   ├── Tasks/         <-- Task operations (Create, List, Get, Update, ChangeStatus, Assign, Delete, Restore)
│   ├── Comments/      <-- Comment operations (CreateComment, ListComments, DeleteComment)
│   └── Dev/           <-- Local token issuer (POST /dev/token for testing)
│
├── Domain/            <-- Pure business logic (Entities, Enums, Invariants, Errors)
│   ├── Entities/      <-- Project, ProjectMember, TaskItem, TaskComment (Guid v7 IDs)
│   ├── Enums/         <-- ProjectRole, TaskItemStatus, TaskPriority
│   └── Errors/        <-- DomainErrors taxonomy
│
├── Persistence/       <-- Relational database infrastructure
│   ├── AppDbContext.cs<-- EF Core 10 DbContext with Named Query Filters
│   ├── Configurations/<-- EntityTypeConfiguration mappings
│   ├── Interceptors/  <-- AuditTimestampInterceptor, DbCommandCountInterceptor
│   └── Migrations/    <-- EF Core database migration files
│
├── Common/            <-- Shared utility primitives (governed by Rule of Three)
│   ├── Results/       <-- Result<T> monad and RFC 9457 ProblemDetails mapping
│   ├── Endpoints/     <-- IEndpoint interface definition
│   ├── Caching/       <-- HybridCache wrapper and cache keys
│   ├── Pagination/    <-- Keyset (cursor-based) pagination records
│   ├── Security/      <-- ICurrentUser abstraction
│   └── Authorization/ <-- IProjectAccessService (project role enforcement)
│
└── Infrastructure/    <-- System plumbing
    ├── Background/    <-- SoftDeletePurgeService background worker
    ├── Commands/      <-- Database migration & seed CLI runners
    ├── Middleware/    <-- StrictJsonMiddleware, SecurityHeadersMiddleware
    └── OpenApi/       <-- OpenAPI v3 and Scalar documentation setup
```

---

## 4. Anatomy of a Vertical Slice: Why Everything Lives in One File

In `Features/<Feature>/<UseCase>.cs`, each use case is a `public static class` containing 5 tightly integrated nested types:

```csharp
public static class CreateTask
{
    // 1. Request: The immutable input data needed for this use case
    public sealed record Request(Guid ProjectId, string Title, string? Description, ...);

    // 2. Response: The immutable output data returned to the caller
    public sealed record Response(Guid Id, Guid ProjectId, string Title, ...);

    // 3. Validator: FluentValidation rules specific to this request
    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        }
    }

    // 4. Handler: The business orchestration logic (loads domain, saves to DB)
    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, IProjectAccessService projectAccess)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken ct)
        {
            // Verify access -> Validate invariants -> Mutate DB -> Return Result
        }
    }

    // 5. Endpoint: The HTTP routing, authorization policy, and OpenAPI metadata
    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/projects/{projectId:guid}/tasks", async (...) => { ... })
               .RequireAuthorization(AuthorizationPolicies.TasksWrite)
               .RequireRateLimiting(RateLimitingExtensions.MutationPolicy);
        }
    }
}
```

### Why this is better:
- **Zero mental friction:** When you need to understand how "Create Task" works, you open `CreateTask.cs` and read it from top to bottom. You never need to jump between 6 files in different projects.
- **Easy deletion:** If a business feature is retired, deleting `CreateTask.cs` removes the endpoint, validator, handler, and DTOs in a single commit with zero dangling artifacts.
- **Tailored queries:** If a read use case needs only two columns, it writes `.Select(x => new Response(x.Id, x.Title))`. It is not forced to load entire entity graphs through a generic repository.

---

## 5. What is "Scaffolding a New Vertical Slice"?

In traditional Clean Architecture, adding a feature is tedious because you have to create many files by hand. 

To eliminate that friction, I created a scaffolding script:
- Windows: [`scripts/new-slice.ps1`](file:///d:/Projects/Code/ant/VerticalSliceMinimalApi/scripts/new-slice.ps1)
- Linux/macOS: [`scripts/new-slice.sh`](file:///d:/Projects/Code/ant/VerticalSliceMinimalApi/scripts/new-slice.sh)

### How It Works
When you want to add a new use case, say `ExportProjects` under the `Projects` feature, you run:

```bash
./scripts/new-slice.ps1 Projects ExportProjects
```

The script:
1. Reads the architectural blueprint from [`docs/slice-template.cs.txt`](file:///d:/Projects/Code/ant/VerticalSliceMinimalApi/docs/slice-template.cs.txt).
2. Replaces placeholders with `Projects` and `ExportProjects`.
3. Creates `src/TaskTracker.Api/Features/Projects/ExportProjects.cs` with pre-wired `Request`, `Response`, `Validator`, `Handler`, and `Endpoint` classes.
4. Because the API uses assembly scanning at startup, **you don't need to register it anywhere!** The moment you save the file and run `dotnet run`, the new route is automatically mapped and available.

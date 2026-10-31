# 19. Dedicated Migration CLI Lifecycle Over Startup Auto-Migration

Date: 2026-10-31
Status: Accepted

## Context

Many .NET applications execute `dbContext.Database.Migrate()` at application startup inside `Program.cs`. In production Kubernetes or containerized environments, this practice introduces severe failure modes:
1. **Race Conditions & Deadlocks:** When multiple API replicas scale up simultaneously, all replicas attempt to acquire migration locks concurrently, resulting in migration failures, connection timeouts, or deadlocked PostgreSQL metadata tables.
2. **Elevated Privileges:** The API service account must be granted full DDL permissions (`CREATE TABLE`, `ALTER TABLE`, `DROP INDEX`) rather than minimal DML permissions (`SELECT`, `INSERT`, `UPDATE`, `DELETE`), violating the principle of least privilege.
3. **Uncontrolled Blast Radius:** If a migration fails or takes hours to backfill a massive table, application startup crashes, taking down the entire web cluster.

## Decision

I chose to **separate database schema migrations into a dedicated CLI command (`dotnet TaskTracker.Api.dll --migrate`) invoked as an independent one-shot job**.

1. In production, database migrations are executed by a Kubernetes `Job` or Docker Compose init service (`tasktracker-migrate`) prior to starting web replicas.
2. Application startup *never* runs DDL migrations automatically.
3. The API includes built-in CLI command switches handled before the web host starts:
   - `--migrate`: Applies pending EF Core migrations and exits with code 0 (or 1 on failure).
   - `--seed-demo`: Seeds sample projects, members, tasks, and comments for local exploratory testing.
   - `--seed-perf`: Seeds large-volume datasets for performance benchmarking.
   - `--healthcheck`: Invokes internal health check logic for container health verification in chiseled distroless images.

## Consequences

### Positive
- **Zero Startup Contention:** Web replicas boot instantly without checking or applying migrations.
- **Least Privilege Compatibility:** In production, the web API connection string requires only DML permissions, while the migration job runs under a DDL role.
- **Deterministic Deployment:** In CI/CD pipelines, if migrations fail, the deployment aborts *before* routing traffic to new application versions.

### Negative
- Requires configuring a pre-deployment step or init container in deployment manifests (demonstrated in `compose.yaml`).

### Alternatives Considered
- **Startup `Database.Migrate()`:** Explicitly rejected as an anti-pattern for multi-replica production environments.
- **Raw SQL Migration Scripts via Flyway/Liquibase:** Viable, but EF Core CLI execution keeps migration definitions in C# code without adding external tooling dependencies.

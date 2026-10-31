# 17. Real Infrastructure Testing with Testcontainers and Respawn

Date: 2026-10-31
Status: Accepted

## Context

Automated testing strategies often choose between two extremes:
1. **Mock-heavy unit tests:** Fast, but test mocks rather than real system behavior. Mocks cannot catch SQL syntax errors, migration issues, PostgreSQL GIN index behavior, or Redis connection drops.
2. **In-memory databases (`UseInMemoryDatabase` or SQLite in-memory):** EF Core's in-memory provider does not enforce foreign keys, unique constraints, transactions, or PostgreSQL-specific full-text search and array functions (`websearch_to_tsquery`, `@>`).

## Decision

I chose **integration testing against real PostgreSQL and Valkey containers managed by Testcontainers.NET and reset via Respawn**.

1. **Testcontainers Lifecycle:**
   - A single shared `ContainersFixture` manages the lifecycle of genuine `postgres:17.11-alpine` and `valkey/valkey:8.0.12-alpine` containers across the test run.
   - EF Core migrations run once at test assembly initialization.
2. **Fast Isolation with Respawn:**
   - Instead of tearing down and recreating databases for every test (which takes seconds), `Respawn.Checkpoint` resets all relational tables in milliseconds between tests.
   - Valkey caches are flushed concurrently via `FLUSHALL`.
3. **Deterministic Multi-Instance Testing:**
   - Multi-instance cache tests launch two distinct `CustomWebApplicationFactory` instances pointing to the same containerized PostgreSQL and Valkey backends, verifying distributed tag invalidation across process boundaries.

## Consequences

### Positive
- **100% Behavioral Fidelity:** Tests execute against the exact database engine and cache server used in production.
- **Zero Drift:** Tests validate real database constraints, foreign keys, migrations, and PostgreSQL full-text search syntax.
- **Speed:** Respawn table truncation allows hundreds of integration tests to complete in seconds.

### Negative
- Running tests requires a running Docker daemon (or Testcontainers Cloud). If Docker is unavailable, tests report clear skipping or diagnostic messages.

### Alternatives Considered
- **EF Core In-Memory Provider:** Rejected as fundamentally misleading; cannot execute raw SQL, stored functions, or transaction isolation.
- **SQLite In-Memory:** Rejected due to lack of PostgreSQL-specific features (`tsvector`, array columns, GIN indexes).

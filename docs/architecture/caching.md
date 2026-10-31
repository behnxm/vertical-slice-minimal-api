# Two-Tier Caching Architecture (HybridCache & Valkey)

This document describes the caching strategy, stampede protection mechanisms, tag-based invalidation, and failure resilience implemented in the Task Tracker API.

---

## Topology: L1 Process Memory + L2 Valkey

The API utilizes .NET 10's `Microsoft.Extensions.Caching.HybridCache` configured with:
- **L1 (In-Memory):** Ultra-fast, zero-network, process-local memory cache.
- **L2 (Distributed Valkey):** High-performance, BSD-3 licensed, Redis-compatible distributed cache shared across all running API instances.

```
[ Incoming HTTP Request ]
            ↓
    [ HybridCache ]
      ├── L1 Memory Cache ── (Hit) ──> Return in < 0.1ms
      └── L2 Valkey Cache  ── (Hit) ──> Populate L1 & Return in < 1ms
               ↓ (Miss)
    [ Execute Factory / Database Query ]
               ↓
    [ Coalesced Population of L1 & L2 ]
```

---

## Stampede Protection (Request Coalescing)

A classic pathology in distributed caching is the **Cache Stampede**: when a popular key expires or is invalidated, hundreds of concurrent requests simultaneously query the primary database, resulting in CPU spikes, connection pool exhaustion, and cascading outages.

`HybridCache` solves this natively through **in-process request coalescing**:
- When multiple threads or requests request an expired or missing key, exactly **one** execution of the factory delegate is dispatched.
- All other concurrent callers await that single execution and share the resulting value once returned.

---

## Tag-Based Cache Invalidation

Cache entries are tagged by entity identifiers and parent container keys:
- Task items are tagged with `task:{id}` and `project:{projectId}:tasks`.
- Project details are tagged with `project:{id}`.

When a mutating operation executes (`UpdateTask`, `ChangeTaskStatus`, `DeleteTask`):
```csharp
await cacheService.RemoveByTagAsync(CacheConstants.TaskTag(taskId), ct);
await cacheService.RemoveByTagAsync(CacheConstants.ProjectTasksTag(projectId), ct);
```
This guarantees that all related queries across all API instances sharing the Valkey L2 cache are purged instantaneously.

---

## Fail-Open Resilience & Degradation Handling

Caching is an optimization, not a hard operational requirement:
1. If the Valkey connection drops, `CacheService` catches the exception, increments the `tasktracker.cache.failures` custom counter, logs a warning, and falls back to executing the factory delegate against PostgreSQL.
2. The `/health/ready` check honestly reports the cache status as **Degraded** rather than Unhealthy:
   - Database down = **Unhealthy (HTTP 503)** (API cannot function).
   - Valkey cache down = **Degraded (HTTP 200)** (API functions via primary database fallback).

---

## Cache Keys & Expiration Conventions

| Target | Cache Key Pattern | L1 Expiration | L2 Expiration | Tags |
| :--- | :--- | :--- | :--- | :--- |
| **Project Details** | `project:{id}` | 5 minutes | 15 minutes | `project:{id}` |
| **Task Details** | `task:{id}` | 5 minutes | 15 minutes | `task:{id}`, `project:{projectId}:tasks` |
| **Project Membership**| `member:{projectId}:{userId}`| 2 minutes | 10 minutes | `project:{projectId}:members` |

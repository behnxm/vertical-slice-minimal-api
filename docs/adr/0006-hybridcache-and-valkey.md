# 6. HybridCache and Valkey L2 Cache Topology

Date: 2026-10-31
Status: Accepted

## Context

Distributed caching in .NET has historically relied on `IDistributedCache`. However, `IDistributedCache` exhibits well-known production issues:
1. **Cache Stampedes:** When a popular key expires, hundreds of concurrent requests simultaneously query the primary database, causing catastrophic CPU and connection spikes.
2. **Serialization Overhead:** Every cache hit incurs deserialization costs, even when reading data that was retrieved milliseconds prior.
3. **No Native Tagging:** Invalidation of related cache keys (e.g., all tasks belonging to a specific project) requires ad-hoc key prefix tracking.

.NET 9/10 introduced `HybridCache`, a unified two-level caching abstraction designed to address these limitations.

## Decision

I chose **Microsoft.Extensions.Caching.HybridCache** with an **in-memory L1** and a **Valkey (Redis-compatible) L2**.

1. Valkey (BSD-3 licensed fork of Redis) provides the persistent, multi-instance distributed L2 cache.
2. HybridCache automatically serializes data to Valkey while keeping hot entries in process memory (L1).
3. Stampede protection is built-in: concurrent cache misses for the same key await the first factory execution rather than hammering the database.
4. Cache tag invalidation (`InvalidateByTagAsync`) is used across mutation slices to invalidate entity collections instantaneously.
5. Invalidation errors and Valkey connectivity drops fail open: the application logs a warning and falls back to primary database queries rather than failing client requests.
6. Custom meters track L1/L2 hits, misses, and stampede locks.

## Consequences

### Positive
- Sub-millisecond reads on L1 cache hits.
- Total protection against cache stampedes via built-in request coalescing.
- Clean cluster invalidation across multiple API instances sharing the same Valkey instance.
- Fully compliant BSD-3 license (avoiding Redis SSPL/RSAL license restrictions).

### Negative
- Requires maintaining both L1 and L2 expiration policies.
- Types cached in HybridCache must be serializable (satisfied by our immutable C# records).

### Alternatives Considered
- **IDistributedCache (StackExchange.Redis):** Rejected due to lack of stampede protection and high L2 network serialization churn.
- **FusionCache:** Excellent third-party library, but HybridCache is now the official Microsoft LTS standard in .NET 10.

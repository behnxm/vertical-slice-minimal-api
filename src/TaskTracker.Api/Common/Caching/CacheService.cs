using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace TaskTracker.Api.Common.Caching;

public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);
}

public interface ICacheInvalidator
{
    Task InvalidateProjectAsync(Guid projectId, string userId, CancellationToken cancellationToken = default);
}

public interface ICacheHealthTracker
{
    bool IsDegraded { get; }
    void MarkDegraded();
    void MarkHealthy();
}

public sealed class CacheHealthTracker : ICacheHealthTracker
{
    private volatile bool _isDegraded;
    public bool IsDegraded => _isDegraded;
    public void MarkDegraded() => _isDegraded = true;
    public void MarkHealthy() => _isDegraded = false;
}

public sealed partial class CacheService : ICacheService, ICacheInvalidator
{
    private static readonly Meter CacheMeter = new("TaskTracker.Cache", "1.0.0");
    private static readonly Counter<long> CacheHitsCounter = CacheMeter.CreateCounter<long>("tasktracker_cache_hits_total");
    private static readonly Counter<long> CacheMissesCounter = CacheMeter.CreateCounter<long>("tasktracker_cache_misses_total");
    private static readonly Counter<long> CacheErrorsCounter = CacheMeter.CreateCounter<long>("tasktracker_cache_errors_total");

    private readonly HybridCache _hybridCache;
    private readonly ICacheHealthTracker _healthTracker;
    private readonly ILogger<CacheService> _logger;

    public CacheService(
        HybridCache hybridCache,
        ICacheHealthTracker healthTracker,
        ILogger<CacheService> logger)
    {
        _hybridCache = hybridCache;
        _healthTracker = healthTracker;
        _logger = logger;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var factoryExecuted = false;

        async ValueTask<T> UnderlyingFactory(CancellationToken ct)
        {
            factoryExecuted = true;
            return await factory(ct);
        }

        try
        {
            var options = new HybridCacheEntryOptions
            {
                Expiration = expiration ?? CacheConstants.DefaultL2Expiration,
                LocalCacheExpiration = CacheConstants.DefaultL1Expiration
            };

            var result = await _hybridCache.GetOrCreateAsync(
                key,
                UnderlyingFactory,
                options,
                tags,
                cancellationToken);

            _healthTracker.MarkHealthy();

            if (factoryExecuted)
            {
                CacheMissesCounter.Add(1, new KeyValuePair<string, object?>("key_prefix", GetPrefix(key)));
            }
            else
            {
                CacheHitsCounter.Add(1, new KeyValuePair<string, object?>("key_prefix", GetPrefix(key)));
            }

            return result;
        }
        catch (Exception ex)
        {
            // HybridCache or L2 failed (e.g. Valkey outage or serialization issue).
            // Fall back honestly to executing the database factory directly.
            CacheErrorsCounter.Add(1, new KeyValuePair<string, object?>("key_prefix", GetPrefix(key)));
            _healthTracker.MarkDegraded();
            LogCacheReadFailed(_logger, ex, key);

            return await factory(cancellationToken);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hybridCache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            CacheErrorsCounter.Add(1);
            _healthTracker.MarkDegraded();
            LogCacheRemoveFailed(_logger, ex, key);
        }
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hybridCache.RemoveByTagAsync(tag, cancellationToken);
        }
        catch (Exception ex)
        {
            CacheErrorsCounter.Add(1);
            _healthTracker.MarkDegraded();
            LogCacheRemoveTagFailed(_logger, ex, tag);
        }
    }

    public async Task InvalidateProjectAsync(Guid projectId, string userId, CancellationToken cancellationToken = default)
    {
        // Multi-layered invalidation strategy:
        // 1. Explicit key removals
        await RemoveAsync(CacheConstants.ProjectKey(projectId), cancellationToken);
        await RemoveAsync(CacheConstants.ProjectSummaryKey(projectId), cancellationToken);
        await RemoveAsync(CacheConstants.UserProjectsFirstPageKey(userId), cancellationToken);

        // 2. Tag invalidations for related entities
        await RemoveByTagAsync(CacheConstants.ProjectTag(projectId), cancellationToken);
        await RemoveByTagAsync(CacheConstants.UserTag(userId), cancellationToken);
    }

    private static string GetPrefix(string key)
    {
        var firstColon = key.IndexOf(':');
        if (firstColon < 0) return "generic";
        var secondColon = key.IndexOf(':', firstColon + 1);
        return secondColon > 0 ? key[..secondColon] : key[..firstColon];
    }

    [LoggerMessage(LogLevel.Warning, "Cache read failed for key {CacheKey}. Falling back directly to database factory.")]
    private static partial void LogCacheReadFailed(ILogger logger, Exception ex, string cacheKey);

    [LoggerMessage(LogLevel.Warning, "Failed to remove cache key {CacheKey}.")]
    private static partial void LogCacheRemoveFailed(ILogger logger, Exception ex, string cacheKey);

    [LoggerMessage(LogLevel.Warning, "Failed to remove cache tag {CacheTag}.")]
    private static partial void LogCacheRemoveTagFailed(ILogger logger, Exception ex, string cacheTag);
}

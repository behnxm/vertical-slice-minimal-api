using System.Text.Json;
using StackExchange.Redis;

namespace TaskTracker.Api.Common.Idempotency;

public sealed class ValkeyIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private readonly IDatabase _database = redis.GetDatabase();

    private sealed class StoredEntry
    {
        public required string State { get; set; }
        public required string PayloadHash { get; set; }
        public int StatusCode { get; set; }
        public string? ContentType { get; set; }
        public string? Body { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new();
    }

    public async Task<IdempotencyAcquireResult> TryAcquireAsync(
        string key,
        string payloadHash,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var redisKey = $"idempotency:{key}";

        var inProgressEntry = new StoredEntry
        {
            State = "in_progress",
            PayloadHash = payloadHash
        };

        var inProgressJson = JsonSerializer.Serialize(inProgressEntry);

        // Atomic SETNX with TTL (When.NotExists)
        var acquired = await _database.StringSetAsync(redisKey, inProgressJson, ttl, When.NotExists);
        if (acquired)
        {
            return new IdempotencyAcquireResult(IdempotencyAcquireStatus.Acquired);
        }

        // Key already exists: inspect stored state
        var existingValue = await _database.StringGetAsync(redisKey);
        if (existingValue.IsNullOrEmpty)
        {
            // Expired between commands, try again
            var retry = await _database.StringSetAsync(redisKey, inProgressJson, ttl, When.NotExists);
            return retry
                ? new IdempotencyAcquireResult(IdempotencyAcquireStatus.Acquired)
                : new IdempotencyAcquireResult(IdempotencyAcquireStatus.InProgress);
        }

        var entry = JsonSerializer.Deserialize<StoredEntry>(existingValue.ToString());
        if (entry is null)
        {
            return new IdempotencyAcquireResult(IdempotencyAcquireStatus.InProgress);
        }

        if (string.Equals(entry.State, "in_progress", StringComparison.Ordinal))
        {
            return new IdempotencyAcquireResult(IdempotencyAcquireStatus.InProgress);
        }

        if (!string.Equals(entry.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return new IdempotencyAcquireResult(IdempotencyAcquireStatus.PayloadMismatch);
        }

        var cachedResponse = new IdempotencyCachedResponse(
            entry.StatusCode,
            entry.ContentType,
            entry.Body,
            entry.Headers);

        return new IdempotencyAcquireResult(IdempotencyAcquireStatus.Completed, cachedResponse);
    }

    public async Task CompleteAsync(
        string key,
        string payloadHash,
        int statusCode,
        string? contentType,
        string? body,
        IReadOnlyDictionary<string, string> headers,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var redisKey = $"idempotency:{key}";

        var completedEntry = new StoredEntry
        {
            State = "completed",
            PayloadHash = payloadHash,
            StatusCode = statusCode,
            ContentType = contentType,
            Body = body,
            Headers = headers.ToDictionary(k => k.Key, v => v.Value)
        };

        var completedJson = JsonSerializer.Serialize(completedEntry);
        await _database.StringSetAsync(redisKey, completedJson, ttl, When.Always);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        var redisKey = $"idempotency:{key}";
        await _database.KeyDeleteAsync(redisKey);
    }
}

using System.Collections.Concurrent;

namespace TaskTracker.Api.Common.Idempotency;

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private sealed class StoredEntry
    {
        public required string State { get; set; }
        public required string PayloadHash { get; set; }
        public int StatusCode { get; set; }
        public string? ContentType { get; set; }
        public string? Body { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new();
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private readonly ConcurrentDictionary<string, StoredEntry> _entries = new(StringComparer.Ordinal);

    public Task<IdempotencyAcquireResult> TryAcquireAsync(
        string key,
        string payloadHash,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (_entries.TryGetValue(key, out var existing))
        {
            if (existing.ExpiresAt < now)
            {
                _entries.TryRemove(key, out _);
            }
            else
            {
                if (string.Equals(existing.State, "in_progress", StringComparison.Ordinal))
                {
                    return Task.FromResult(new IdempotencyAcquireResult(IdempotencyAcquireStatus.InProgress));
                }

                if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                {
                    return Task.FromResult(new IdempotencyAcquireResult(IdempotencyAcquireStatus.PayloadMismatch));
                }

                var cachedResponse = new IdempotencyCachedResponse(
                    existing.StatusCode,
                    existing.ContentType,
                    existing.Body,
                    existing.Headers);

                return Task.FromResult(new IdempotencyAcquireResult(IdempotencyAcquireStatus.Completed, cachedResponse));
            }
        }

        var entry = new StoredEntry
        {
            State = "in_progress",
            PayloadHash = payloadHash,
            ExpiresAt = now.Add(ttl)
        };

        if (_entries.TryAdd(key, entry))
        {
            return Task.FromResult(new IdempotencyAcquireResult(IdempotencyAcquireStatus.Acquired));
        }

        return Task.FromResult(new IdempotencyAcquireResult(IdempotencyAcquireStatus.InProgress));
    }

    public Task CompleteAsync(
        string key,
        string payloadHash,
        int statusCode,
        string? contentType,
        string? body,
        IReadOnlyDictionary<string, string> headers,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var entry = new StoredEntry
        {
            State = "completed",
            PayloadHash = payloadHash,
            StatusCode = statusCode,
            ContentType = contentType,
            Body = body,
            Headers = headers.ToDictionary(k => k.Key, v => v.Value),
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
        };

        _entries[key] = entry;
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

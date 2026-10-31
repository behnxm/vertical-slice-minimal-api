namespace TaskTracker.Api.Common.Idempotency;

public enum IdempotencyAcquireStatus
{
    Acquired,
    InProgress,
    Completed,
    PayloadMismatch
}

public sealed record IdempotencyAcquireResult(
    IdempotencyAcquireStatus Status,
    IdempotencyCachedResponse? CachedResponse = null);

public sealed record IdempotencyCachedResponse(
    int StatusCode,
    string? ContentType,
    string? Body,
    IReadOnlyDictionary<string, string> Headers);

public interface IIdempotencyStore
{
    Task<IdempotencyAcquireResult> TryAcquireAsync(
        string key,
        string payloadHash,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        string key,
        string payloadHash,
        int statusCode,
        string? contentType,
        string? body,
        IReadOnlyDictionary<string, string> headers,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);
}

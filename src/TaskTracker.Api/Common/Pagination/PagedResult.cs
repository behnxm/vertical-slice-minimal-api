namespace TaskTracker.Api.Common.Pagination;

/// <summary>
/// Keyset paginated response container.
/// Total count is intentionally omitted: counting large sets degrades database performance
/// and encourages inefficient OFFSET queries, whereas keyset pagination guarantees O(1) page access.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasMore);

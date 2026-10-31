# 9. Keyset Cursor Pagination Over Offset/Limit

Date: 2026-10-31
Status: Accepted

## Context

APIs serving collection resources commonly implement offset-based pagination via `OFFSET` and `LIMIT` (or LINQ `.Skip(x).Take(y)`). However, offset pagination suffers from two severe flaws in high-traffic APIs:
1. **O(N) Database Degradation:** As the page number grows (e.g. `OFFSET 100000`), the database engine must scan and discard 100,000 rows before returning the requested 20 rows, resulting in high I/O and query latency.
2. **Missing and Duplicate Rows ("Pagination Drift"):** If new items are inserted or deleted while a client is traversing pages, rows shift across page boundaries, causing clients to see duplicate items or miss items entirely.

## Decision

I chose **keyset (cursor-based) pagination** as the default pagination strategy for all collection endpoints (`ListProjects`, `ListTasks`, `ListComments`).

1. Cursors are encoded as opaque URL-safe Base64 tokens containing the primary sort timestamp and unique entity ID (e.g. `2026-10-31T20:00:00Z|0192e4a8-6f10-7000-8000-000000000000`).
2. Slices evaluate cursor predicates using indexed tuple comparisons:
   ```csharp
   query = query.Where(t => t.CreatedAtUtc < cursorCreatedAt || 
       (t.CreatedAtUtc == cursorCreatedAt && t.Id < cursorId));
   ```
3. Queries order deterministically: `.OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.Id)`.
4. Slices request `limit + 1` rows to determine whether a next page exists without performing an expensive `COUNT(*)` query.
5. Slices project a `nextCursor` string and a `hasMore` boolean in response payloads.

## Consequences

### Positive
- **O(1) Constant Performance:** Query execution time remains sub-millisecond regardless of table size or how deep a client paginates.
- **Drift Immunity:** Inserts and deletes do not shift page windows; records are anchored by timestamp and sequential UUIDv7.
- **No Costly `COUNT(*)`:** Eliminates database table/index scans to calculate total item counts.

### Negative
- Random page jumping (e.g. "Jump directly to page 47") is not supported; clients traverse sequentially.
- Requires composite indexes covering `(created_at DESC, id DESC)`.

### Alternatives Considered
- **Offset/Limit (`Skip`/`Take`):** Explicitly rejected due to quadratic performance decay and data drift anomalies.

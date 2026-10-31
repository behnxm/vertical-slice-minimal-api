using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Api.Common.Caching;
using TaskTracker.Api.Common.Endpoints;
using TaskTracker.Api.Common.Filters;
using TaskTracker.Api.Common.Pagination;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Errors;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Features.Projects;

public static class ListProjects
{
    public sealed record Request(
        string? Cursor = null,
        int Limit = 25);

    public sealed record ProjectItem(
        Guid Id,
        string Name,
        string? Description,
        DateTimeOffset CreatedAt,
        string CreatedBy,
        DateTimeOffset? ArchivedAt,
        uint RowVersion);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Limit)
                .InclusiveBetween(1, 100)
                .WithMessage("Limit must be between 1 and 100.");
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        ICurrentUser currentUser,
        ICacheService cacheService)
    {
        public async Task<Result<PagedResult<ProjectItem>>> HandleAsync(
            Request request,
            CancellationToken cancellationToken)
        {
            if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
            {
                return DomainErrors.Security.Unauthorized;
            }

            var userId = currentUser.UserId;

            // Keyset pagination decoding
            (DateTimeOffset SortKey, Guid Id)? cursorState = null;
            if (!string.IsNullOrWhiteSpace(request.Cursor))
            {
                var decodeResult = Common.Pagination.Cursor.TryDecode(request.Cursor);
                if (decodeResult.IsFailure)
                {
                    return decodeResult.Error;
                }
                cursorState = decodeResult.Value;
            }

            // Cache rule: The first page (no cursor) is cached in HybridCache. Cursor pages bypass cache.
            if (cursorState is null && request.Limit == 25)
            {
                var cacheKey = CacheConstants.UserProjectsFirstPageKey(userId);
                var userTag = CacheConstants.UserTag(userId);

                return await cacheService.GetOrCreateAsync(
                    cacheKey,
                    async ct => await FetchProjectsPageAsync(cursorState, request.Limit, ct),
                    CacheConstants.DefaultL2Expiration,
                    [userTag],
                    cancellationToken);
            }

            return await FetchProjectsPageAsync(cursorState, request.Limit, cancellationToken);
        }

        private async Task<PagedResult<ProjectItem>> FetchProjectsPageAsync(
            (DateTimeOffset SortKey, Guid Id)? cursorState,
            int limit,
            CancellationToken ct)
        {
            // The EF Core 10 Membership filter automatically scopes this query to the current user's projects.
            var query = dbContext.Projects
                .AsNoTracking();

            if (cursorState.HasValue)
            {
                var (cursorTime, cursorId) = cursorState.Value;
                query = query.Where(p =>
                    p.CreatedAt < cursorTime ||
                    (p.CreatedAt == cursorTime && p.Id < cursorId));
            }

            var items = await query
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Take(limit + 1)
                .Select(p => new ProjectItem(
                    p.Id,
                    p.Name,
                    p.Description,
                    p.CreatedAt,
                    p.CreatedBy,
                    p.ArchivedAt,
                    p.RowVersion))
                .ToListAsync(ct);

            var hasMore = items.Count > limit;
            var resultItems = hasMore ? items.Take(limit).ToList() : items;

            string? nextCursor = null;
            if (hasMore && resultItems.Count > 0)
            {
                var lastItem = resultItems[^1];
                nextCursor = Common.Pagination.Cursor.Encode(lastItem.CreatedAt, lastItem.Id);
            }

            return new PagedResult<ProjectItem>(resultItems, nextCursor, hasMore);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/projects", async (
                [AsParameters] Request request,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                if (result.IsSuccess)
                {
                    httpContext.Response.Headers.CacheControl = "private, no-cache";
                }
                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthConstants.Policies.Read)
            .RequireRateLimiting("read")
            .AddEndpointFilter<ValidationFilter<Request>>()
            .WithTags("Projects")
            .WithSummary("List Projects")
            .WithDescription("Returns keyset paginated projects accessible to the current user. First page is cached in HybridCache.")
            .Produces<PagedResult<ProjectItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

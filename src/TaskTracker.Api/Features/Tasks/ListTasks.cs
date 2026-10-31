using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using TaskTracker.Api.Common.Authorization;
using TaskTracker.Api.Common.Endpoints;
using TaskTracker.Api.Common.Filters;
using TaskTracker.Api.Common.Pagination;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Features.Tasks;

public static class ListTasks
{
    public sealed record Request(
        Guid ProjectId,
        TaskItemStatus? Status = null,
        TaskPriority? Priority = null,
        string? Assignee = null,
        DateTimeOffset? DueBefore = null,
        DateTimeOffset? DueAfter = null,
        string? Label = null,
        string? Search = null,
        string? Cursor = null,
        int Limit = 20);

    public sealed record TaskSummary(
        Guid Id,
        Guid ProjectId,
        string Title,
        string? Description,
        TaskItemStatus Status,
        TaskPriority Priority,
        DateTimeOffset? DueDate,
        string? AssigneeUserId,
        string[] Labels,
        DateTimeOffset CreatedAt,
        string CreatedBy,
        DateTimeOffset? UpdatedAt,
        DateTimeOffset? CompletedAt,
        uint RowVersion);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("Project identifier is required.");

            RuleFor(x => x.Limit)
                .InclusiveBetween(1, 100).WithMessage("Limit must be between 1 and 100.");

            RuleFor(x => x.Search)
                .MaximumLength(200).WithMessage("Search query must not exceed 200 characters.")
                .When(x => !string.IsNullOrEmpty(x.Search));
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess)
    {
        public async Task<Result<PagedResult<TaskSummary>>> HandleAsync(
            Request request,
            CancellationToken cancellationToken)
        {
            var accessResult = await projectAccess.EnsureAccessAsync(
                request.ProjectId,
                ProjectRole.Viewer,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                return accessResult.Error;
            }

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

            var query = dbContext.Tasks
                .AsNoTracking()
                .Where(t => t.ProjectId == request.ProjectId);

            if (request.Status.HasValue)
            {
                query = query.Where(t => t.Status == request.Status.Value);
            }

            if (request.Priority.HasValue)
            {
                query = query.Where(t => t.Priority == request.Priority.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.Assignee))
            {
                var assignee = request.Assignee.Trim();
                query = query.Where(t => t.AssigneeUserId == assignee);
            }

            if (request.DueBefore.HasValue)
            {
                var dueBefore = request.DueBefore.Value.ToUniversalTime();
                query = query.Where(t => t.DueDate <= dueBefore);
            }

            if (request.DueAfter.HasValue)
            {
                var dueAfter = request.DueAfter.Value.ToUniversalTime();
                query = query.Where(t => t.DueDate >= dueAfter);
            }

            if (!string.IsNullOrWhiteSpace(request.Label))
            {
                var labelSlug = request.Label.Trim().ToLowerInvariant();
                query = query.Where(t => t.Labels.Contains(labelSlug));
            }

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var search = request.Search.Trim();
                query = query.Where(t => EF.Property<NpgsqlTsVector>(t, "SearchVector")
                    .Matches(EF.Functions.WebSearchToTsQuery("english", search)));
            }

            if (cursorState.HasValue)
            {
                var (cursorTime, cursorId) = cursorState.Value;
                query = query.Where(t =>
                    t.CreatedAt < cursorTime ||
                    (t.CreatedAt == cursorTime && t.Id < cursorId));
            }

            var items = await query
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Take(request.Limit + 1)
                .Select(t => new TaskSummary(
                    t.Id,
                    t.ProjectId,
                    t.Title,
                    t.Description,
                    t.Status,
                    t.Priority,
                    t.DueDate,
                    t.AssigneeUserId,
                    t.Labels,
                    t.CreatedAt,
                    t.CreatedBy,
                    t.UpdatedAt,
                    t.CompletedAt,
                    t.RowVersion))
                .ToListAsync(cancellationToken);

            var hasMore = items.Count > request.Limit;
            var resultItems = hasMore ? items.Take(request.Limit).ToList() : items;

            string? nextCursor = null;
            if (hasMore && resultItems.Count > 0)
            {
                var lastItem = resultItems[^1];
                nextCursor = Common.Pagination.Cursor.Encode(lastItem.CreatedAt, lastItem.Id);
            }

            return new PagedResult<TaskSummary>(resultItems, nextCursor, hasMore);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/projects/{projectId:guid}/tasks", async (
                Guid projectId,
                [AsParameters] QueryParameters queryParams,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(
                    projectId,
                    queryParams.Status,
                    queryParams.Priority,
                    queryParams.Assignee,
                    queryParams.DueBefore,
                    queryParams.DueAfter,
                    queryParams.Label,
                    queryParams.Search,
                    queryParams.Cursor,
                    queryParams.Limit ?? 20);

                var result = await handler.HandleAsync(request, ct);
                if (result.IsSuccess)
                {
                    httpContext.Response.Headers.CacheControl = "private, no-cache";
                }
                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthConstants.Policies.Read)
            .RequireRateLimiting("read")
            .WithTags("Tasks")
            .WithSummary("List Tasks")
            .WithDescription("Returns keyset paginated tasks in a project with optional full-text search, label filtering, status, priority, and date range filters.")
            .Produces<PagedResult<TaskSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }

    public sealed record QueryParameters(
        TaskItemStatus? Status = null,
        TaskPriority? Priority = null,
        string? Assignee = null,
        DateTimeOffset? DueBefore = null,
        DateTimeOffset? DueAfter = null,
        string? Label = null,
        string? Search = null,
        string? Cursor = null,
        int? Limit = null);
}

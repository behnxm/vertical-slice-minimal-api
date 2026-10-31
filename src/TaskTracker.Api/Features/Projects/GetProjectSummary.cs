using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Api.Common.Authorization;
using TaskTracker.Api.Common.Caching;
using TaskTracker.Api.Common.Endpoints;
using TaskTracker.Api.Common.Filters;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Domain.Errors;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Features.Projects;

public static class GetProjectSummary
{
    public sealed record Request(Guid ProjectId);

    public sealed record Response(
        Guid ProjectId,
        int TotalTasks,
        int TodoCount,
        int InProgressCount,
        int DoneCount,
        int OverdueCount,
        IReadOnlyDictionary<string, int> PriorityCounts);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("Project identifier is required.");
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess,
        ICacheService cacheService,
        TimeProvider timeProvider)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            // Security check: Validate membership against DB first
            var accessResult = await projectAccess.EnsureAccessAsync(
                request.ProjectId,
                ProjectRole.Viewer,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                return accessResult.Error;
            }

            var cacheKey = CacheConstants.ProjectSummaryKey(request.ProjectId);
            var projectTag = CacheConstants.ProjectTag(request.ProjectId);

            var summary = await cacheService.GetOrCreateAsync(
                cacheKey,
                async ct =>
                {
                    var now = timeProvider.GetUtcNow();

                    var tasks = await dbContext.Tasks
                        .AsNoTracking()
                        .Where(t => t.ProjectId == request.ProjectId)
                        .Select(t => new { t.Status, t.Priority, t.DueDate })
                        .ToListAsync(ct);

                    var total = tasks.Count;
                    var todo = tasks.Count(t => t.Status == TaskItemStatus.Todo);
                    var inProgress = tasks.Count(t => t.Status == TaskItemStatus.InProgress);
                    var done = tasks.Count(t => t.Status == TaskItemStatus.Done);
                    var overdue = tasks.Count(t => t.Status != TaskItemStatus.Done && t.DueDate.HasValue && t.DueDate.Value < now);

                    var priorities = tasks
                        .GroupBy(t => t.Priority.ToString())
                        .ToDictionary(g => g.Key, g => g.Count());

                    // Ensure all priority keys exist in dictionary
                    foreach (var priority in Enum.GetNames<TaskPriority>())
                    {
                        if (!priorities.ContainsKey(priority))
                        {
                            priorities[priority] = 0;
                        }
                    }

                    return new Response(
                        request.ProjectId,
                        total,
                        todo,
                        inProgress,
                        done,
                        overdue,
                        priorities);
                },
                CacheConstants.DefaultL2Expiration,
                [projectTag],
                cancellationToken);

            return summary;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/projects/{projectId:guid}/summary", async (
                Guid projectId,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(projectId);
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
            .WithSummary("Get Project Summary")
            .WithDescription("Retrieves aggregated task metrics and priority distribution for a project. Cached in HybridCache.")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

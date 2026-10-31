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

namespace TaskTracker.Api.Features.Tasks;

public static class GetTask
{
    public sealed record Request(Guid TaskId);

    public sealed record Response(
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
            RuleFor(x => x.TaskId)
                .NotEmpty().WithMessage("Task identifier is required.");
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess,
        ICacheService cacheService)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var taskMeta = await dbContext.Tasks
                .AsNoTracking()
                .Where(t => t.Id == request.TaskId)
                .Select(t => new { t.ProjectId })
                .FirstOrDefaultAsync(cancellationToken);

            if (taskMeta is null)
            {
                return DomainErrors.Tasks.NotFound;
            }

            var accessResult = await projectAccess.EnsureAccessAsync(
                taskMeta.ProjectId,
                ProjectRole.Viewer,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                // Return 404 to avoid leaking task existence
                return DomainErrors.Tasks.NotFound;
            }

            var cacheKey = CacheConstants.TaskKey(request.TaskId);
            var taskTag = CacheConstants.TaskTag(request.TaskId);

            var response = await cacheService.GetOrCreateAsync(
                cacheKey,
                async ct =>
                {
                    var task = await dbContext.Tasks
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == request.TaskId, ct);

                    if (task is null)
                    {
                        return null;
                    }

                    return new Response(
                        task.Id,
                        task.ProjectId,
                        task.Title,
                        task.Description,
                        task.Status,
                        task.Priority,
                        task.DueDate,
                        task.AssigneeUserId,
                        task.Labels,
                        task.CreatedAt,
                        task.CreatedBy,
                        task.UpdatedAt,
                        task.CompletedAt,
                        task.RowVersion);
                },
                CacheConstants.DefaultL2Expiration,
                [taskTag],
                cancellationToken);

            return response is not null ? response : DomainErrors.Tasks.NotFound;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/tasks/{taskId:guid}", async (
                Guid taskId,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(taskId);
                var result = await handler.HandleAsync(request, ct);
                if (result.IsFailure)
                {
                    return result.ToHttpResult();
                }

                var task = result.Value;
                if (ETagHelper.IsNotModified(httpContext, task.RowVersion))
                {
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                ETagHelper.SetETagAndCacheControl(httpContext, task.RowVersion);
                return TypedResults.Ok(task);
            })
            .RequireAuthorization(AuthConstants.Policies.Read)
            .RequireRateLimiting("read")
            .WithTags("Tasks")
            .WithSummary("Get Task")
            .WithDescription("Retrieves detailed task information with weak ETag conditional caching. Requires Viewer role.")
            .Produces<Response>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

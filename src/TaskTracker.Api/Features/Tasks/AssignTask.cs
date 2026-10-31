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

public static class AssignTask
{
    public sealed record Request(Guid TaskId, string? AssigneeUserId);

    public sealed record Body(string? AssigneeUserId);

    public sealed record Response(
        Guid Id,
        Guid ProjectId,
        string? AssigneeUserId,
        DateTimeOffset? UpdatedAt,
        uint RowVersion);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty().WithMessage("Task identifier is required.");

            RuleFor(x => x.AssigneeUserId)
                .MaximumLength(128).WithMessage("Assignee user identifier must not exceed 128 characters.")
                .When(x => !string.IsNullOrEmpty(x.AssigneeUserId));
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess,
        ICacheInvalidator cacheInvalidator,
        TimeProvider timeProvider)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var task = await dbContext.Tasks
                .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken);

            if (task is null)
            {
                return DomainErrors.Tasks.NotFound;
            }

            var accessResult = await projectAccess.EnsureAccessAsync(
                task.ProjectId,
                ProjectRole.Editor,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                return DomainErrors.Tasks.NotFound;
            }

            var isArchived = await dbContext.Projects
                .AsNoTracking()
                .Where(p => p.Id == task.ProjectId)
                .Select(p => p.IsArchived)
                .FirstOrDefaultAsync(cancellationToken);

            if (isArchived)
            {
                return DomainErrors.Projects.ArchivedReadOnly;
            }

            if (!string.IsNullOrWhiteSpace(request.AssigneeUserId))
            {
                var isMember = await dbContext.ProjectMembers
                    .AnyAsync(
                        m => m.ProjectId == task.ProjectId && m.UserId == request.AssigneeUserId,
                        cancellationToken);

                if (!isMember)
                {
                    return DomainErrors.Tasks.AssigneeNotMember;
                }
            }

            task.Assign(request.AssigneeUserId, timeProvider);
            await dbContext.SaveChangesAsync(cancellationToken);

            await cacheInvalidator.InvalidateTaskAsync(task.ProjectId, task.Id, cancellationToken);

            return new Response(
                task.Id,
                task.ProjectId,
                task.AssigneeUserId,
                task.UpdatedAt,
                task.RowVersion);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPut("/tasks/{taskId:guid}/assignee", async (
                Guid taskId,
                Body body,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(taskId, body.AssigneeUserId);
                var result = await handler.HandleAsync(request, ct);
                if (result.IsFailure)
                {
                    return result.ToHttpResult();
                }

                ETagHelper.SetETagAndCacheControl(httpContext, result.Value.RowVersion);
                return TypedResults.Ok(result.Value);
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .WithTags("Tasks")
            .WithSummary("Assign Task")
            .WithDescription("Assigns or unassigns a task. Assigned user must be a member of the containing project. Requires Editor role.")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

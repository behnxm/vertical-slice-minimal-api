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
using TaskTracker.Api.Domain.Entities;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Domain.Errors;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Features.Tasks;

public static class CreateTask
{
    public sealed record Request(
        Guid ProjectId,
        string Title,
        string? Description,
        TaskPriority Priority,
        DateTimeOffset? DueDate,
        string? AssigneeUserId,
        string[]? Labels);

    public sealed record Body(
        string Title,
        string? Description,
        TaskPriority? Priority,
        DateTimeOffset? DueDate,
        string? AssigneeUserId,
        string[]? Labels);

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
            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("Project identifier is required.");

            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Task title is required.")
                .MaximumLength(200).WithMessage("Task title must not exceed 200 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(4000).WithMessage("Task description must not exceed 4000 characters.")
                .When(x => x.Description is not null);

            RuleFor(x => x.Priority)
                .IsInEnum().WithMessage("A valid task priority must be specified.");

            RuleFor(x => x.AssigneeUserId)
                .MaximumLength(128).WithMessage("Assignee user identifier must not exceed 128 characters.")
                .When(x => x.AssigneeUserId is not null);

            RuleFor(x => x.Labels)
                .Must(labels => labels == null || labels.Length <= 10)
                .WithMessage("A task cannot have more than 10 labels.")
                .Must(labels => labels == null || labels.All(l => !string.IsNullOrWhiteSpace(l) && l.Length <= 30))
                .WithMessage("Each label must be non-empty and not exceed 30 characters.");
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess,
        ICurrentUser currentUser,
        ICacheInvalidator cacheInvalidator,
        TimeProvider timeProvider)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var accessResult = await projectAccess.EnsureAccessAsync(
                request.ProjectId,
                ProjectRole.Editor,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                return accessResult.Error;
            }

            var project = await dbContext.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.ProjectId, cancellationToken);

            if (project is null)
            {
                return DomainErrors.Projects.NotFound;
            }

            if (project.IsArchived)
            {
                return DomainErrors.Projects.ArchivedReadOnly;
            }

            if (!string.IsNullOrWhiteSpace(request.AssigneeUserId))
            {
                var isAssigneeMember = await dbContext.ProjectMembers
                    .AnyAsync(
                        m => m.ProjectId == request.ProjectId && m.UserId == request.AssigneeUserId,
                        cancellationToken);

                if (!isAssigneeMember)
                {
                    return DomainErrors.Tasks.AssigneeNotMember;
                }
            }

            var taskResult = TaskItem.Create(
                request.ProjectId,
                request.Title,
                request.Description,
                request.Priority,
                request.DueDate,
                request.AssigneeUserId,
                request.Labels,
                currentUser.UserId ?? accessResult.Value.UserId,
                timeProvider);

            if (taskResult.IsFailure)
            {
                return taskResult.Error;
            }

            var task = taskResult.Value;
            dbContext.Tasks.Add(task);
            await dbContext.SaveChangesAsync(cancellationToken);

            await cacheInvalidator.InvalidateTaskAsync(task.ProjectId, task.Id, cancellationToken);

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
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/projects/{projectId:guid}/tasks", async (
                Guid projectId,
                Body body,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(
                    projectId,
                    body.Title,
                    body.Description,
                    body.Priority ?? TaskPriority.Medium,
                    body.DueDate,
                    body.AssigneeUserId,
                    body.Labels);

                var result = await handler.HandleAsync(request, ct);
                if (result.IsFailure)
                {
                    return result.ToHttpResult();
                }

                ETagHelper.SetETagAndCacheControl(httpContext, result.Value.RowVersion);
                return TypedResults.Created($"/api/v1/tasks/{result.Value.Id}", result.Value);
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .WithTags("Tasks")
            .WithSummary("Create Task")
            .WithDescription("Creates a new task within a project. Requires Editor or Owner role.")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

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

public static class UpdateTask
{
    public sealed record Request(
        Guid TaskId,
        string Title,
        string? Description,
        TaskPriority Priority,
        DateTimeOffset? DueDate,
        string[]? Labels);

    public sealed record Body(
        string Title,
        string? Description,
        TaskPriority Priority,
        DateTimeOffset? DueDate,
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
            RuleFor(x => x.TaskId)
                .NotEmpty().WithMessage("Task identifier is required.");

            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Task title is required.")
                .MaximumLength(200).WithMessage("Task title must not exceed 200 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(4000).WithMessage("Task description must not exceed 4000 characters.")
                .When(x => x.Description is not null);

            RuleFor(x => x.Priority)
                .IsInEnum().WithMessage("A valid task priority must be specified.");

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
        ICacheInvalidator cacheInvalidator,
        TimeProvider timeProvider)
    {
        public async Task<Result<Response>> HandleAsync(
            Request request,
            string? ifMatch,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(ifMatch))
            {
                return DomainErrors.Concurrency.PreconditionRequired;
            }

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

            var expectedETag = ETagHelper.Format(task.RowVersion);
            var expectedPlain = $"\"{task.RowVersion}\"";
            var matches = ifMatch == "*" ||
                          ifMatch == expectedETag ||
                          ifMatch == expectedPlain ||
                          ifMatch.Contains(expectedETag, StringComparison.Ordinal) ||
                          ifMatch.Contains(expectedPlain, StringComparison.Ordinal);

            if (!matches)
            {
                return DomainErrors.Concurrency.PreconditionFailed;
            }

            var updateResult = task.UpdateDetails(
                request.Title,
                request.Description,
                request.Priority,
                request.DueDate,
                request.Labels,
                timeProvider);

            if (updateResult.IsFailure)
            {
                return updateResult.Error;
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return DomainErrors.Concurrency.Conflict;
            }

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
            app.MapPut("/tasks/{taskId:guid}", async (
                Guid taskId,
                Body body,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(
                    taskId,
                    body.Title,
                    body.Description,
                    body.Priority,
                    body.DueDate,
                    body.Labels);

                var ifMatch = httpContext.Request.Headers.IfMatch.ToString();
                var result = await handler.HandleAsync(request, ifMatch, ct);
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
            .WithSummary("Update Task")
            .WithDescription("Updates task details. Requires Editor role and matching If-Match header.")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Api.Common.Authorization;
using TaskTracker.Api.Common.Endpoints;
using TaskTracker.Api.Common.Filters;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Entities;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Domain.Errors;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Features.Comments;

public static class CreateComment
{
    public sealed record Request(Guid TaskId, string Body);

    public sealed record BodyPayload(string Body);

    public sealed record Response(
        Guid Id,
        Guid TaskId,
        string AuthorUserId,
        string Body,
        DateTimeOffset CreatedAt);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty().WithMessage("Task identifier is required.");

            RuleFor(x => x.Body)
                .NotEmpty().WithMessage("Comment body is required.")
                .MaximumLength(2000).WithMessage("Comment body must not exceed 2000 characters.");
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var taskMeta = await dbContext.Tasks
                .AsNoTracking()
                .Where(t => t.Id == request.TaskId)
                .Select(t => new { t.Id, t.ProjectId })
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
                return DomainErrors.Tasks.NotFound;
            }

            var isArchived = await dbContext.Projects
                .AsNoTracking()
                .Where(p => p.Id == taskMeta.ProjectId)
                .Select(p => p.IsArchived)
                .FirstOrDefaultAsync(cancellationToken);

            if (isArchived)
            {
                return DomainErrors.Projects.ArchivedReadOnly;
            }

            var authorUserId = currentUser.UserId ?? accessResult.Value.UserId;
            var commentResult = TaskComment.Create(
                request.TaskId,
                authorUserId,
                request.Body,
                timeProvider);

            if (commentResult.IsFailure)
            {
                return commentResult.Error;
            }

            var comment = commentResult.Value;
            dbContext.Comments.Add(comment);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new Response(
                comment.Id,
                comment.TaskId,
                comment.AuthorUserId,
                comment.Body,
                comment.CreatedAt);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/tasks/{taskId:guid}/comments", async (
                Guid taskId,
                BodyPayload body,
                Handler handler,
                CancellationToken ct) =>
            {
                var request = new Request(taskId, body.Body);
                var result = await handler.HandleAsync(request, ct);
                if (result.IsFailure)
                {
                    return result.ToHttpResult();
                }

                return TypedResults.Created($"/api/v1/tasks/{taskId}/comments/{result.Value.Id}", result.Value);
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .AddEndpointFilter<IdempotencyFilter>()
            .WithTags("Comments")
            .WithSummary("Create Task Comment")
            .WithDescription("Adds a comment to a task. Any project member (Viewer, Editor, Owner) can comment on accessible tasks.")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

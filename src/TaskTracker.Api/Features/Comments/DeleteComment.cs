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
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Domain.Errors;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Features.Comments;

public static class DeleteComment
{
    public sealed record Request(Guid TaskId, Guid CommentId);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty().WithMessage("Task identifier is required.");

            RuleFor(x => x.CommentId)
                .NotEmpty().WithMessage("Comment identifier is required.");
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess,
        ICurrentUser currentUser)
    {
        public async Task<Result> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var comment = await dbContext.Comments
                .Include(c => c.Task)
                .FirstOrDefaultAsync(
                    c => c.Id == request.CommentId && c.TaskId == request.TaskId,
                    cancellationToken);

            if (comment is null || comment.Task is null)
            {
                return DomainErrors.Comments.NotFound;
            }

            var accessResult = await projectAccess.EnsureAccessAsync(
                comment.Task.ProjectId,
                ProjectRole.Viewer,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                return DomainErrors.Comments.NotFound;
            }

            var isAuthor = string.Equals(currentUser.UserId, comment.AuthorUserId, StringComparison.Ordinal);
            var isOwner = accessResult.Value.Role == ProjectRole.Owner;

            if (!isAuthor && !isOwner)
            {
                return DomainErrors.Comments.DeleteForbidden;
            }

            dbContext.Comments.Remove(comment);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("/tasks/{taskId:guid}/comments/{commentId:guid}", async (
                Guid taskId,
                Guid commentId,
                Handler handler,
                CancellationToken ct) =>
            {
                var request = new Request(taskId, commentId);
                var result = await handler.HandleAsync(request, ct);
                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .WithTags("Comments")
            .WithSummary("Delete Task Comment")
            .WithDescription("Permanently deletes a task comment. Allowed only for the comment author or project owners.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

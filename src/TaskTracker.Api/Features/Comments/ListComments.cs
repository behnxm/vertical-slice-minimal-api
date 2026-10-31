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

public static class ListComments
{
    public sealed record Request(Guid TaskId);

    public sealed record CommentItem(
        Guid Id,
        Guid TaskId,
        string AuthorUserId,
        string Body,
        DateTimeOffset CreatedAt);

    public sealed record Response(
        Guid TaskId,
        IReadOnlyList<CommentItem> Comments);

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
        IProjectAccessService projectAccess)
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

            var comments = await dbContext.Comments
                .AsNoTracking()
                .Where(c => c.TaskId == request.TaskId)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new CommentItem(
                    c.Id,
                    c.TaskId,
                    c.AuthorUserId,
                    c.Body,
                    c.CreatedAt))
                .ToListAsync(cancellationToken);

            return new Response(request.TaskId, comments);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/tasks/{taskId:guid}/comments", async (
                Guid taskId,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(taskId);
                var result = await handler.HandleAsync(request, ct);
                if (result.IsSuccess)
                {
                    httpContext.Response.Headers.CacheControl = "private, no-cache";
                }
                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthConstants.Policies.Read)
            .RequireRateLimiting("read")
            .WithTags("Comments")
            .WithSummary("List Task Comments")
            .WithDescription("Retrieves chronological comments on a task. Requires Viewer role on the project.")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

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

public static class RestoreTask
{
    public sealed record Request(Guid TaskId);

    public sealed record Response(
        Guid Id,
        Guid ProjectId,
        string Title,
        TaskItemStatus Status,
        DateTimeOffset? DeletedAt,
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
        ICacheInvalidator cacheInvalidator)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            // Allowlisted: RestoreTask requires querying across deleted records and cross-project boundaries for tasks.admin role
            var task = await dbContext.Tasks
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken);

            if (task is null)
            {
                return DomainErrors.Tasks.NotFound;
            }

            if (task.DeletedAt is null)
            {
                return DomainErrors.Tasks.NotDeleted;
            }

            task.Restore();
            await dbContext.SaveChangesAsync(cancellationToken);

            await cacheInvalidator.InvalidateTaskAsync(task.ProjectId, task.Id, cancellationToken);

            return new Response(
                task.Id,
                task.ProjectId,
                task.Title,
                task.Status,
                task.DeletedAt,
                task.RowVersion);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/admin/tasks/{taskId:guid}/restore", async (
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

                ETagHelper.SetETagAndCacheControl(httpContext, result.Value.RowVersion);
                return TypedResults.Ok(result.Value);
            })
            .RequireAuthorization(AuthConstants.Policies.Admin)
            .RequireRateLimiting("write")
            .WithTags("Tasks")
            .WithSummary("Restore Deleted Task")
            .WithDescription("Restores a soft-deleted task. Requires admin authority with tasks.admin scope.")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

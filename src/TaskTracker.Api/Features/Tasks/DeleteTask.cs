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

public static class DeleteTask
{
    public sealed record Request(Guid TaskId);

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
        ICacheInvalidator cacheInvalidator,
        TimeProvider timeProvider)
    {
        public async Task<Result> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var task = await dbContext.Tasks
                .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken);

            if (task is null)
            {
                return DomainErrors.Tasks.NotFound;
            }

            var accessResult = await projectAccess.EnsureAccessAsync(
                task.ProjectId,
                ProjectRole.Owner,
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

            task.SoftDelete(timeProvider);
            await dbContext.SaveChangesAsync(cancellationToken);

            await cacheInvalidator.InvalidateTaskAsync(task.ProjectId, task.Id, cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("/tasks/{taskId:guid}", async (
                Guid taskId,
                Handler handler,
                CancellationToken ct) =>
            {
                var request = new Request(taskId);
                var result = await handler.HandleAsync(request, ct);
                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .WithTags("Tasks")
            .WithSummary("Delete Task")
            .WithDescription("Soft-deletes a task. Requires Project Owner role.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

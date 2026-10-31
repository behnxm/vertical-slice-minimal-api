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

namespace TaskTracker.Api.Features.Members;

public static class RemoveMember
{
    public sealed record Request(Guid ProjectId, string UserId);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("Project identifier is required.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("User identifier is required.");
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess,
        ICurrentUser currentUser,
        ICacheInvalidator cacheInvalidator)
    {
        public async Task<Result> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var isSelfRemoval = string.Equals(currentUser.UserId, request.UserId, StringComparison.Ordinal);

            // Self-removal requires project membership (Viewer+); removing others requires Owner role
            var requiredRole = isSelfRemoval ? ProjectRole.Viewer : ProjectRole.Owner;
            var accessResult = await projectAccess.EnsureAccessAsync(request.ProjectId, requiredRole, cancellationToken);
            if (accessResult.IsFailure)
            {
                return accessResult.Error;
            }

            var targetMember = await dbContext.ProjectMembers
                .FirstOrDefaultAsync(
                    m => m.ProjectId == request.ProjectId && m.UserId == request.UserId,
                    cancellationToken);

            if (targetMember is null)
            {
                return DomainErrors.Members.NotFound;
            }

            // Invariant: Cannot remove the last remaining owner (even if leaving voluntarily)
            if (targetMember.Role == ProjectRole.Owner)
            {
                var ownerCount = await dbContext.ProjectMembers
                    .CountAsync(
                        m => m.ProjectId == request.ProjectId && m.Role == ProjectRole.Owner,
                        cancellationToken);

                if (ownerCount <= 1)
                {
                    return DomainErrors.Members.LastOwnerCannotBeRemoved;
                }
            }

            dbContext.ProjectMembers.Remove(targetMember);
            await dbContext.SaveChangesAsync(cancellationToken);

            await cacheInvalidator.InvalidateProjectAsync(request.ProjectId, request.UserId, cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("/projects/{projectId:guid}/members/{userId}", async (
                Guid projectId,
                string userId,
                Handler handler,
                CancellationToken ct) =>
            {
                var request = new Request(projectId, userId);
                var result = await handler.HandleAsync(request, ct);
                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .WithTags("Members")
            .WithSummary("Remove Project Member")
            .WithDescription("Removes a user from the project. Permitted for the project Owner or as self-removal. Prohibits removing the last Owner.")
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

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

public static class UpdateMemberRole
{
    public sealed record Request(
        Guid ProjectId,
        string UserId,
        ProjectRole Role);

    public sealed record Body(ProjectRole Role);

    public sealed record Response(
        Guid ProjectId,
        string UserId,
        ProjectRole Role);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("Project identifier is required.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("User identifier is required.");

            RuleFor(x => x.Role)
                .IsInEnum().WithMessage("A valid project role must be specified.");
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess,
        ICacheInvalidator cacheInvalidator)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            // Only Project Owners can manage roles
            var accessResult = await projectAccess.EnsureAccessAsync(
                request.ProjectId,
                ProjectRole.Owner,
                cancellationToken);

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

            // Invariant: The last remaining Owner cannot be demoted to another role
            if (targetMember.Role == ProjectRole.Owner && request.Role != ProjectRole.Owner)
            {
                var ownerCount = await dbContext.ProjectMembers
                    .CountAsync(
                        m => m.ProjectId == request.ProjectId && m.Role == ProjectRole.Owner,
                        cancellationToken);

                if (ownerCount <= 1)
                {
                    return DomainErrors.Members.LastOwnerCannotBeDemoted;
                }
            }

            targetMember.UpdateRole(request.Role);
            await dbContext.SaveChangesAsync(cancellationToken);

            await cacheInvalidator.InvalidateProjectAsync(request.ProjectId, request.UserId, cancellationToken);

            return new Response(targetMember.ProjectId, targetMember.UserId, targetMember.Role);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPut("/projects/{projectId:guid}/members/{userId}", async (
                Guid projectId,
                string userId,
                Body body,
                Handler handler,
                CancellationToken ct) =>
            {
                var request = new Request(projectId, userId, body.Role);
                var result = await handler.HandleAsync(request, ct);
                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .WithTags("Members")
            .WithSummary("Update Member Role")
            .WithDescription("Changes a project member's role. Prohibits demoting the project's last owner. Requires Owner role.")
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

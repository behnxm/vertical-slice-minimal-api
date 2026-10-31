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

namespace TaskTracker.Api.Features.Members;

public static class AddMember
{
    public sealed record Request(
        Guid ProjectId,
        string UserId,
        ProjectRole Role);

    public sealed record Body(
        string UserId,
        ProjectRole Role);

    public sealed record Response(
        Guid ProjectId,
        string UserId,
        ProjectRole Role,
        DateTimeOffset AddedAt,
        string AddedBy);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("Project identifier is required.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("User identifier is required.")
                .MaximumLength(128).WithMessage("User identifier must not exceed 128 characters.");

            RuleFor(x => x.Role)
                .IsInEnum().WithMessage("A valid project role must be specified.");
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
            // Only Project Owners can manage project members
            var accessResult = await projectAccess.EnsureAccessAsync(
                request.ProjectId,
                ProjectRole.Owner,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                return accessResult.Error;
            }

            var existingMember = await dbContext.ProjectMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.ProjectId == request.ProjectId && m.UserId == request.UserId,
                    cancellationToken);

            if (existingMember is not null)
            {
                return DomainErrors.Members.AlreadyMember;
            }

            var memberResult = ProjectMember.Create(
                request.ProjectId,
                request.UserId,
                request.Role,
                accessResult.Value.UserId,
                timeProvider);

            if (memberResult.IsFailure)
            {
                return memberResult.Error;
            }

            dbContext.ProjectMembers.Add(memberResult.Value);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Invalidate project cache for the newly added member
            await cacheInvalidator.InvalidateProjectAsync(request.ProjectId, request.UserId, cancellationToken);

            var member = memberResult.Value;
            return new Response(
                member.ProjectId,
                member.UserId,
                member.Role,
                member.AddedAt,
                member.AddedBy);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/projects/{projectId:guid}/members", async (
                Guid projectId,
                Body body,
                Handler handler,
                CancellationToken ct) =>
            {
                var request = new Request(projectId, body.UserId, body.Role);
                var result = await handler.HandleAsync(request, ct);
                return result.ToCreatedHttpResult($"/api/v1/projects/{projectId}/members/{body.UserId}");
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .WithTags("Members")
            .WithSummary("Add Project Member")
            .WithDescription("Adds a new member to the project with an assigned role. Requires Owner role.")
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

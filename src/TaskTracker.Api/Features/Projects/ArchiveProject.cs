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

namespace TaskTracker.Api.Features.Projects;

public static class ArchiveProject
{
    public sealed record Request(Guid ProjectId);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("Project identifier is required.");
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
            // Only Project Owners can archive a project
            var accessResult = await projectAccess.EnsureAccessAsync(
                request.ProjectId,
                ProjectRole.Owner,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                return accessResult.Error;
            }

            var project = await dbContext.Projects
                .FirstOrDefaultAsync(p => p.Id == request.ProjectId, cancellationToken);

            if (project is null)
            {
                return DomainErrors.Projects.NotFound;
            }

            var archiveResult = project.Archive(timeProvider);
            if (archiveResult.IsFailure)
            {
                return archiveResult.Error;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            await cacheInvalidator.InvalidateProjectAsync(
                project.Id,
                accessResult.Value.UserId,
                cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/projects/{projectId:guid}/archive", async (
                Guid projectId,
                Handler handler,
                CancellationToken ct) =>
            {
                var request = new Request(projectId);
                var result = await handler.HandleAsync(request, ct);
                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .AddEndpointFilter<ValidationFilter<Request>>()
            .WithTags("Projects")
            .WithSummary("Archive Project")
            .WithDescription("Archives a project, making it read-only for subsequent mutations. Requires Owner role.")
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

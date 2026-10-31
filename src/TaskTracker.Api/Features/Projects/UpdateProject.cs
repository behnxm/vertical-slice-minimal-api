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

public static class UpdateProject
{
    public sealed record Request(
        Guid ProjectId,
        string Name,
        string? Description = null);

    public sealed record Body(
        string Name,
        string? Description = null);

    public sealed record Response(
        Guid Id,
        string Name,
        string? Description,
        DateTimeOffset CreatedAt,
        string CreatedBy,
        DateTimeOffset? UpdatedAt,
        DateTimeOffset? ArchivedAt,
        uint RowVersion);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("Project identifier is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Project name is required.")
                .MaximumLength(100).WithMessage("Project name must not exceed 100 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.");
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
            // Only Project Owners are allowed to modify project settings
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

            // Verify ETag / If-Match optimistic concurrency
            if (string.IsNullOrWhiteSpace(ifMatch))
            {
                return DomainErrors.Concurrency.PreconditionRequired;
            }

            var expectedETag = ETagHelper.Format(project.RowVersion);
            var expectedPlain = $"\"{project.RowVersion}\"";
            var matches = ifMatch == "*" ||
                          ifMatch == expectedETag ||
                          ifMatch == expectedPlain ||
                          ifMatch.Contains(expectedETag, StringComparison.Ordinal) ||
                          ifMatch.Contains(expectedPlain, StringComparison.Ordinal);

            if (!matches)
            {
                return DomainErrors.Concurrency.PreconditionFailed;
            }

            var updateResult = project.Update(request.Name, request.Description, timeProvider);
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

            await cacheInvalidator.InvalidateProjectAsync(
                project.Id,
                accessResult.Value.UserId,
                cancellationToken);

            return new Response(
                project.Id,
                project.Name,
                project.Description,
                project.CreatedAt,
                project.CreatedBy,
                project.UpdatedAt,
                project.ArchivedAt,
                project.RowVersion);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPut("/projects/{projectId:guid}", async (
                Guid projectId,
                Body body,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(projectId, body.Name, body.Description);
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
            .WithTags("Projects")
            .WithSummary("Update Project")
            .WithDescription("Updates project details. Requires Owner role and matching If-Match header.")
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

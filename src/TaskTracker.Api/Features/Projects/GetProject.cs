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

public static class GetProject
{
    public sealed record Request(Guid ProjectId);

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
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        IProjectAccessService projectAccess,
        ICacheService cacheService)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            // Security rule: Always evaluate project membership and role capabilities against the database first.
            var accessResult = await projectAccess.EnsureAccessAsync(
                request.ProjectId,
                ProjectRole.Viewer,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                return accessResult.Error;
            }

            var cacheKey = CacheConstants.ProjectKey(request.ProjectId);
            var projectTag = CacheConstants.ProjectTag(request.ProjectId);

            // Fetch through HybridCache (L1 in-process + L2 Valkey)
            var response = await cacheService.GetOrCreateAsync(
                cacheKey,
                async ct =>
                {
                    var project = await dbContext.Projects
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);

                    if (project is null)
                    {
                        return null;
                    }

                    return new Response(
                        project.Id,
                        project.Name,
                        project.Description,
                        project.CreatedAt,
                        project.CreatedBy,
                        project.UpdatedAt,
                        project.ArchivedAt,
                        project.RowVersion);
                },
                CacheConstants.DefaultL2Expiration,
                [projectTag],
                cancellationToken);

            return response is not null ? response : DomainErrors.Projects.NotFound;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/projects/{projectId:guid}", async (
                Guid projectId,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(projectId);
                var result = await handler.HandleAsync(request, ct);
                if (result.IsFailure)
                {
                    return result.ToHttpResult();
                }

                var project = result.Value;

                // Check If-None-Match conditional header
                if (ETagHelper.IsNotModified(httpContext, project.RowVersion))
                {
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                ETagHelper.SetETagAndCacheControl(httpContext, project.RowVersion);
                return TypedResults.Ok(project);
            })
            .RequireAuthorization(AuthConstants.Policies.Read)
            .RequireRateLimiting("read")
            .AddEndpointFilter<ValidationFilter<Request>>()
            .WithTags("Projects")
            .WithSummary("Get Project Details")
            .WithDescription("Retrieves project details by ID with weak ETag conditional caching.")
            .Produces<Response>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

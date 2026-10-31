using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TaskTracker.Api.Common.Caching;
using TaskTracker.Api.Common.Endpoints;
using TaskTracker.Api.Common.Filters;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Entities;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Domain.Errors;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Features.Projects;

public static class CreateProject
{
    public sealed record Request(
        string Name,
        string? Description = null);

    public sealed record Response(
        Guid Id,
        string Name,
        string? Description,
        DateTimeOffset CreatedAt,
        string CreatedBy,
        uint RowVersion);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Project name is required.")
                .MaximumLength(100).WithMessage("Project name must not exceed 100 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.");
        }
    }

    public sealed class Handler(
        AppDbContext dbContext,
        ICurrentUser currentUser,
        ICacheInvalidator cacheInvalidator,
        TimeProvider timeProvider)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
            {
                return DomainErrors.Security.Unauthorized;
            }

            var projectResult = Project.Create(
                request.Name,
                request.Description,
                currentUser.UserId,
                timeProvider);

            if (projectResult.IsFailure)
            {
                return projectResult.Error;
            }

            var project = projectResult.Value;

            // Domain rule: The project creator automatically becomes an Owner
            var memberResult = ProjectMember.Create(
                project.Id,
                currentUser.UserId,
                ProjectRole.Owner,
                currentUser.UserId,
                timeProvider);

            if (memberResult.IsFailure)
            {
                return memberResult.Error;
            }

            dbContext.Projects.Add(project);
            dbContext.ProjectMembers.Add(memberResult.Value);

            await dbContext.SaveChangesAsync(cancellationToken);

            // Invalidate cached project list for current user
            await cacheInvalidator.InvalidateProjectAsync(project.Id, currentUser.UserId, cancellationToken);

            var response = new Response(
                project.Id,
                project.Name,
                project.Description,
                project.CreatedAt,
                project.CreatedBy,
                project.RowVersion);

            return response;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/projects", async (
                Request request,
                Handler handler,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                return result.ToCreatedHttpResult($"/api/v1/projects/{result.Value?.Id}");
            })
            .RequireAuthorization(AuthConstants.Policies.Write)
            .RequireRateLimiting("write")
            .AddEndpointFilter<ValidationFilter<Request>>()
            .WithTags("Projects")
            .WithSummary("Create Project")
            .WithDescription("Creates a new project workspace. The caller automatically becomes an Owner.")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

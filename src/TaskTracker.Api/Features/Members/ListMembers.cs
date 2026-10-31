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
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Features.Members;

public static class ListMembers
{
    public sealed record Request(Guid ProjectId);

    public sealed record MemberItem(
        string UserId,
        ProjectRole Role,
        DateTimeOffset AddedAt,
        string AddedBy);

    public sealed record Response(
        Guid ProjectId,
        IReadOnlyList<MemberItem> Members);

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
        IProjectAccessService projectAccess)
    {
        public async Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            // Security rule: Only active members of the project can view the member list
            var accessResult = await projectAccess.EnsureAccessAsync(
                request.ProjectId,
                ProjectRole.Viewer,
                cancellationToken);

            if (accessResult.IsFailure)
            {
                return accessResult.Error;
            }

            var members = await dbContext.ProjectMembers
                .AsNoTracking()
                .Where(m => m.ProjectId == request.ProjectId)
                .OrderBy(m => m.AddedAt)
                .Select(m => new MemberItem(
                    m.UserId,
                    m.Role,
                    m.AddedAt,
                    m.AddedBy))
                .ToListAsync(cancellationToken);

            return new Response(request.ProjectId, members);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/projects/{projectId:guid}/members", async (
                Guid projectId,
                Handler handler,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var request = new Request(projectId);
                var result = await handler.HandleAsync(request, ct);
                if (result.IsSuccess)
                {
                    httpContext.Response.Headers.CacheControl = "private, no-cache";
                }
                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthConstants.Policies.Read)
            .RequireRateLimiting("read")
            .AddEndpointFilter<ValidationFilter<Request>>()
            .WithTags("Members")
            .WithSummary("List Project Members")
            .WithDescription("Retrieves all members associated with a project. Requires Viewer role.")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

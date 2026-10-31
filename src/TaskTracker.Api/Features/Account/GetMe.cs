using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TaskTracker.Api.Common.Endpoints;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Common.Security;

namespace TaskTracker.Api.Features.Account;

public static class GetMe
{
    public sealed record Request;

    public sealed record Response(
        string UserId,
        string? DisplayName,
        IReadOnlyCollection<string> Scopes);

    public sealed class Handler(ICurrentUser currentUser)
    {
        public Task<Result<Response>> HandleAsync(CancellationToken cancellationToken)
        {
            if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
            {
                return Task.FromResult(Result<Response>.Failure(
                    Error.Unauthorized("auth.unauthorized", "User is not authenticated.")));
            }

            var response = new Response(
                currentUser.UserId,
                currentUser.DisplayName,
                currentUser.Scopes.ToArray());

            return Task.FromResult(Result.Success(response));
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/me", async (
                Handler handler,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(ct);
                return result.ToHttpResult();
            })
            .RequireAuthorization()
            .RequireRateLimiting("read")
            .WithTags("Account")
            .WithSummary("Get Current User Profile")
            .WithDescription("Returns identity and scope claims from the validated access token.")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        }
    }
}

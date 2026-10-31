using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TaskTracker.Api.Common.Endpoints;
using TaskTracker.Api.Common.Filters;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Common.Security;

namespace TaskTracker.Api.Features.Dev;

/// <summary>
/// Development-only token issuer providing short-lived ES256 JWT access tokens for testing and local demo.
/// Strictly forbidden in Production environments.
/// </summary>
public static class DevToken
{
    public sealed record Request(
        string UserId,
        string? DisplayName = null,
        string[]? Scopes = null);

    public sealed record Response(
        string AccessToken,
        string TokenType,
        int ExpiresIn);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("User ID is required.")
                .MaximumLength(128).WithMessage("User ID must not exceed 128 characters.");

            RuleForEach(x => x.Scopes)
                .Must(s => s is AuthConstants.Scopes.Read or AuthConstants.Scopes.Write or AuthConstants.Scopes.Admin)
                .WithMessage("Allowed scopes are tasks.read, tasks.write, and tasks.admin.");
        }
    }

    public sealed class Handler(
        DevKeyManager keyManager,
        IConfiguration configuration,
        TimeProvider timeProvider)
    {
        public Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var issuer = configuration["DevIssuer:Issuer"] ?? "tasktracker-dev-issuer";
            var audience = configuration["DevIssuer:Audience"] ?? "tasktracker-api";
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var expires = now.AddHours(1);

            var scopes = request.Scopes is { Length: > 0 }
                ? request.Scopes
                : [AuthConstants.Scopes.Read, AuthConstants.Scopes.Write];

            var claims = new List<Claim>
            {
                new("sub", request.UserId),
                new("name", request.DisplayName ?? request.UserId),
                new("scope", string.Join(' ', scopes)),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Issuer = issuer,
                Audience = audience,
                NotBefore = now,
                Expires = expires,
                SigningCredentials = new SigningCredentials(
                    keyManager.SigningKey,
                    SecurityAlgorithms.EcdsaSha256)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var jwt = tokenHandler.WriteToken(token);

            var response = new Response(
                AccessToken: jwt,
                TokenType: "Bearer",
                ExpiresIn: 3600);

            return Task.FromResult(Result.Success(response));
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/dev/token", async (
                Request request,
                Handler handler,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                return result.ToHttpResult();
            })
            .AllowAnonymous()
            .RequireRateLimiting("dev-token")
            .AddEndpointFilter<ValidationFilter<Request>>()
            .WithTags("Development")
            .WithSummary("Generate Development Access Token")
            .WithDescription("DEVELOPMENT ONLY: Issues short-lived ES256 JWT access tokens for local manual testing. Disabled in production.")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

            app.MapGet("/dev/.well-known/jwks.json", (DevKeyManager keyManager) =>
            {
                var jwk = keyManager.GetPublicJwk();
                return Results.Ok(new { keys = new[] { jwk } });
            })
            .AllowAnonymous()
            .WithTags("Development")
            .WithSummary("Development JWKS Public Key")
            .ExcludeFromDescription();
        }
    }
}

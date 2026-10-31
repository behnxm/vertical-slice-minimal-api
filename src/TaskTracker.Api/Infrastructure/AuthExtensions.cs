using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Features.Dev;

namespace TaskTracker.Api.Infrastructure;

public static class AuthExtensions
{
    public static IServiceCollection AddTaskTrackerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var devIssuerEnabled = configuration.GetValue<bool>("DevIssuer:Enabled");
        var externalAuthority = configuration["Authentication:Authority"];
        var audience = configuration["Authentication:Audience"] ?? configuration["DevIssuer:Audience"] ?? "tasktracker-api";

        // Security assertion: Dev issuer is strictly prohibited in Production
        if (devIssuerEnabled && environment.IsProduction())
        {
            throw new InvalidOperationException("CRITICAL SECURITY ERROR: DevIssuer cannot be enabled in a Production environment.");
        }

        // Security assertion: Never enable both an external Authority and the internal DevIssuer simultaneously
        if (devIssuerEnabled && !string.IsNullOrWhiteSpace(externalAuthority))
        {
            throw new InvalidOperationException("CRITICAL CONFIGURATION ERROR: DevIssuer:Enabled and Authentication:Authority cannot both be set.");
        }

        // Prevent WS-Security legacy claim mapping
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        });

        if (devIssuerEnabled)
        {
            var keyPath = configuration["DevIssuer:SigningKeyPath"] ?? "./dev-keys/dev-token-es256.key";
            var devKeyManager = new DevKeyManager(keyPath);
            services.AddSingleton(devKeyManager);

            var devIssuer = configuration["DevIssuer:Issuer"] ?? "tasktracker-dev-issuer";

            authBuilder.AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.RequireHttpsMetadata = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = devIssuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    IssuerSigningKey = devKeyManager.SigningKey,
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256]
                };
            });
        }
        else
        {
            authBuilder.AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.Authority = externalAuthority;
                options.Audience = audience;
                options.RequireHttpsMetadata = !environment.IsDevelopment();

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms =
                    [
                        SecurityAlgorithms.RsaSha256,
                        SecurityAlgorithms.EcdsaSha256,
                        SecurityAlgorithms.RsaSsaPssSha256
                    ]
                };
            });
        }

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(AuthConstants.Policies.Read, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireAssertion(ctx => HasScope(ctx.User, AuthConstants.Scopes.Read)))
            .AddPolicy(AuthConstants.Policies.Write, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireAssertion(ctx => HasScope(ctx.User, AuthConstants.Scopes.Write)))
            .AddPolicy(AuthConstants.Policies.Admin, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireAssertion(ctx => HasScope(ctx.User, AuthConstants.Scopes.Admin)));

        return services;
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        var scopeClaims = user.FindAll("scope").Concat(user.FindAll("scp"));
        foreach (var claim in scopeClaims)
        {
            var scopes = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (scopes.Contains(requiredScope, StringComparer.Ordinal))
            {
                return true;
            }
        }

        // tasks.admin scope automatically implies read and write capabilities
        if (requiredScope is AuthConstants.Scopes.Read or AuthConstants.Scopes.Write)
        {
            foreach (var claim in scopeClaims)
            {
                var scopes = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (scopes.Contains(AuthConstants.Scopes.Admin, StringComparer.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaskTracker.Api.Common.Caching;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Infrastructure;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddTaskTrackerHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
            .AddCheck<CacheHealthCheck>("valkey_cache", tags: ["ready"]);

        return services;
    }

    public static IEndpointRouteBuilder MapTaskTrackerHealthChecks(
        this IEndpointRouteBuilder app,
        IConfiguration configuration)
    {
        var expose = configuration.GetValue("HealthChecks:Expose", true);
        if (!expose)
        {
            return app;
        }

        // Liveness probe: verified process execution
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
            .AllowAnonymous()
            .WithTags("Health")
            .WithSummary("Liveness Probe")
            .ExcludeFromDescription();

        // Readiness probe: verifies PostgreSQL (required) and Valkey cache (degraded if unavailable)
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var response = new
                {
                    status = report.Status.ToString(),
                    duration = report.TotalDuration,
                    entries = report.Entries.ToDictionary(
                        e => e.Key,
                        e => new
                        {
                            status = e.Value.Status.ToString(),
                            description = e.Value.Description,
                            duration = e.Value.Duration
                        })
                };
                await context.Response.WriteAsJsonAsync(response);
            }
        })
        .AllowAnonymous()
        .WithTags("Health")
        .WithSummary("Readiness Probe")
        .ExcludeFromDescription();

        return app;
    }
}

public sealed class CacheHealthCheck(ICacheHealthTracker healthTracker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (healthTracker.IsDegraded)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "L2 Valkey cache is unreachable or experiencing serialization failures; fallback DB mode active."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("L2 Valkey cache is responsive."));
    }
}

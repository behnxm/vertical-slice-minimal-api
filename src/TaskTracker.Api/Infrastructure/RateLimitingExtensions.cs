using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using TaskTracker.Api.Common.Results;

namespace TaskTracker.Api.Infrastructure;

/// <summary>
/// Configures partition-based sliding window rate limiters.
/// Partitions by authenticated user id when available, falling back to remote IP address.
/// Returns RFC 9457 Problem Details with Retry-After header on rejection.
/// Note: This is an in-memory, per-instance rate limiter. In a multi-instance deployment,
/// a distributed rate limiter (such as Redis-based token bucket or reverse proxy gateway limiter)
/// should be used if global rate enforcement across instances is required.
/// </summary>
public static class RateLimitingExtensions
{
    public static IServiceCollection AddTaskTrackerRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfterSeconds = 60;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterSpan))
                {
                    retryAfterSeconds = Math.Max(1, (int)retryAfterSpan.TotalSeconds);
                }

                context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

                var error = Error.TooManyRequests(
                    "rate_limit.exceeded",
                    $"Too many requests. Please retry after {retryAfterSeconds} seconds.");

                var problem = ResultExtensions.MapErrorToProblemDetails(error);
                await problem.ExecuteAsync(context.HttpContext);
            };

            // Read policy: 300 requests per minute
            options.AddPolicy("read", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    }));

            // Write policy: 60 requests per minute
            options.AddPolicy("write", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    }));

            // Search policy: 30 requests per minute
            options.AddPolicy("search", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    }));

            // Dev token policy: 10 requests per minute strictly partitioned by IP
            options.AddPolicy("dev-token", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    }));

            // Global fallback IP limiter: 600 requests per minute
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 600,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    private static string GetPartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue("sub") ??
                     httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}";
    }
}

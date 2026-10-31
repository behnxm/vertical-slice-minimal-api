using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskTracker.Api.Common.Results;

namespace TaskTracker.Api.Infrastructure;

/// <summary>
/// Enforces strict JSON serialization policies:
/// - Disallows unknown or unmapped members (prevents mass-assignment over-posting attacks).
/// - Enforces camelCase naming.
/// - Requires string enum representation (rejects arbitrary integers).
/// - Enforces maximum depth and rejects string-to-number type coercion.
/// - Caps request body size at 64 KB to mitigate resource exhaustion (DoS).
/// </summary>
public static class JsonExtensions
{
    public const int MaxRequestBodySizeBytes = 64 * 1024; // 64 KB

    public static IServiceCollection ConfigureStrictJson(this IServiceCollection services)
    {
        services.Configure<JsonOptions>(options =>
        {
            ApplyStrictJsonOptions(options.SerializerOptions);
        });

        return services;
    }

    public static void ApplyStrictJsonOptions(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.MaxDepth = 32;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Clear();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }
}

/// <summary>
/// Middleware enforcing request body size limit and Content-Type validation.
/// </summary>
public sealed class RequestHardeningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Enforce 64 KB body size limit
        if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > JsonExtensions.MaxRequestBodySizeBytes)
        {
            var error = Error.Failure("request.body_too_large", "Request payload exceeds the maximum allowed size of 64 KB.");
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.14",
                title = "Payload Too Large",
                status = 413,
                detail = error.Description,
                code = error.Code
            });
            return;
        }

        // Validate JSON content type on POST/PUT requests with a body
        if (context.Request.Method is "POST" or "PUT" &&
            context.Request.ContentLength is > 0)
        {
            var contentType = context.Request.ContentType;
            if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
                    title = "Unsupported Media Type",
                    status = 415,
                    detail = "Content-Type must be application/json.",
                    code = "request.unsupported_media_type"
                });
                return;
            }
        }

        await next(context);
    }
}

public static class RequestHardeningMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestHardening(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestHardeningMiddleware>();
    }
}

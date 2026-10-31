using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskTracker.Api.Common.Idempotency;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Domain.Errors;
using TaskTracker.Api.Infrastructure;

namespace TaskTracker.Api.Common.Filters;

/// <summary>
/// Endpoint filter enforcing 24-hour Valkey-backed idempotency for state-mutating requests.
/// Caches the HTTP status code, response headers, and response body for exact replay.
/// Prohibits payload mutation under an existing key (422) and protects against concurrent execution (409).
/// </summary>
public sealed partial class IdempotencyFilter(
    IIdempotencyStore store,
    IOptions<JsonOptions> jsonOptions,
    ILogger<IdempotencyFilter> logger) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var headerValues) ||
            string.IsNullOrWhiteSpace(headerValues))
        {
            // If the header is omitted, proceed without idempotency guarantees
            return await next(context);
        }

        var key = headerValues.ToString().Trim();
        if (key.Length is < 1 or > 128)
        {
            return ResultExtensions.MapErrorToProblemDetails(
                Error.Validation("idempotency.invalid_key", "Idempotency-Key header must be between 1 and 128 characters."));
        }

        // Read and hash the request body using seekable stream position
        string bodyText = string.Empty;
        if (httpContext.Request.Body.CanSeek)
        {
            httpContext.Request.Body.Position = 0;
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
            bodyText = await reader.ReadToEndAsync(httpContext.RequestAborted);
            httpContext.Request.Body.Position = 0;
        }

        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.Value ?? string.Empty;
        var payloadHash = ComputePayloadHash(method, path, bodyText);

        var acquireResult = await store.TryAcquireAsync(key, payloadHash, DefaultTtl, httpContext.RequestAborted);

        switch (acquireResult.Status)
        {
            case IdempotencyAcquireStatus.PayloadMismatch:
                return ResultExtensions.MapErrorToProblemDetails(DomainErrors.Idempotency.Mismatch);

            case IdempotencyAcquireStatus.InProgress:
                return ResultExtensions.MapErrorToProblemDetails(DomainErrors.Idempotency.InProgress);

            case IdempotencyAcquireStatus.Completed when acquireResult.CachedResponse is not null:
                var cached = acquireResult.CachedResponse;
                httpContext.Response.Headers["Idempotency-Replay"] = "true";
                foreach (var (headerKey, headerVal) in cached.Headers)
                {
                    httpContext.Response.Headers[headerKey] = headerVal;
                }
                return Microsoft.AspNetCore.Http.Results.Content(
                    cached.Body ?? string.Empty,
                    cached.ContentType ?? "application/json",
                    Encoding.UTF8,
                    cached.StatusCode);

            case IdempotencyAcquireStatus.Acquired:
            default:
                break;
        }

        try
        {
            var result = await next(context);

            if (result is not null)
            {
                await SaveResponseAsync(httpContext, key, payloadHash, result);
            }

            return result;
        }
        catch (Exception ex)
        {
            // On unhandled server fault, release the lock so the client can retry
            LogReleaseOnException(logger, ex, key);
            await store.ReleaseAsync(key, CancellationToken.None);
            throw;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Request failed with unhandled exception; releasing idempotency key {IdempotencyKey}")]
    private static partial void LogReleaseOnException(ILogger logger, Exception ex, string idempotencyKey);

    private async Task SaveResponseAsync(
        HttpContext httpContext,
        string key,
        string payloadHash,
        object result)
    {
        var statusCode = StatusCodes.Status200OK;
        string? bodyJson = null;
        var contentType = "application/json";

        if (result is IStatusCodeHttpResult statusResult && statusResult.StatusCode.HasValue)
        {
            statusCode = statusResult.StatusCode.Value;
        }

        if (result is IValueHttpResult valueResult && valueResult.Value is not null)
        {
            bodyJson = JsonSerializer.Serialize(valueResult.Value, jsonOptions.Value.SerializerOptions);
        }
        else if (result is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problemResult)
        {
            statusCode = problemResult.StatusCode;
            bodyJson = JsonSerializer.Serialize(problemResult.ProblemDetails, jsonOptions.Value.SerializerOptions);
            contentType = "application/problem+json";
        }

        // Only cache deterministic client responses (1xx - 4xx); discard transient 5xx errors
        if (statusCode >= 500)
        {
            await store.ReleaseAsync(key, CancellationToken.None);
            return;
        }

        var headersToCache = new Dictionary<string, string>();
        if (httpContext.Response.Headers.TryGetValue("Location", out var location))
        {
            headersToCache["Location"] = location.ToString();
        }
        if (httpContext.Response.Headers.TryGetValue("ETag", out var etag))
        {
            headersToCache["ETag"] = etag.ToString();
        }

        await store.CompleteAsync(
            key,
            payloadHash,
            statusCode,
            contentType,
            bodyJson,
            headersToCache,
            DefaultTtl,
            httpContext.RequestAborted);
    }

    private static string ComputePayloadHash(string method, string path, string body)
    {
        var raw = $"{method.ToUpperInvariant()}:{path.ToLowerInvariant()}:{body}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hashBytes);
    }
}

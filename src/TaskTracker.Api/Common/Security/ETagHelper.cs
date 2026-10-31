using Microsoft.AspNetCore.Http;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Domain.Errors;

namespace TaskTracker.Api.Common.Security;

/// <summary>
/// Helper for generating weak ETags and evaluating conditional request headers (If-None-Match, If-Match).
/// </summary>
public static class ETagHelper
{
    public static string Format(uint rowVersion) => $"W/\"{rowVersion}\"";

    public static void SetETagAndCacheControl(HttpContext context, uint rowVersion)
    {
        context.Response.Headers.ETag = Format(rowVersion);
        context.Response.Headers.CacheControl = "private, no-cache";
    }

    public static bool IsNotModified(HttpContext context, uint rowVersion)
    {
        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        var expectedETag = Format(rowVersion);
        var expectedVersionString = $"\"{rowVersion}\"";

        return ifNoneMatch == "*" ||
               ifNoneMatch == expectedETag ||
               ifNoneMatch == expectedVersionString ||
               ifNoneMatch.Contains(expectedETag, StringComparison.Ordinal) ||
               ifNoneMatch.Contains(expectedVersionString, StringComparison.Ordinal);
    }

    public static Result ValidateIfMatch(HttpContext context, uint currentRowVersion)
    {
        var ifMatch = context.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return DomainErrors.Concurrency.PreconditionRequired;
        }

        var expectedETag = Format(currentRowVersion);
        var expectedVersionString = $"\"{currentRowVersion}\"";

        var matches = ifMatch == "*" ||
                      ifMatch == expectedETag ||
                      ifMatch == expectedVersionString ||
                      ifMatch.Contains(expectedETag, StringComparison.Ordinal) ||
                      ifMatch.Contains(expectedVersionString, StringComparison.Ordinal);

        return matches ? Result.Success() : DomainErrors.Concurrency.PreconditionFailed;
    }
}

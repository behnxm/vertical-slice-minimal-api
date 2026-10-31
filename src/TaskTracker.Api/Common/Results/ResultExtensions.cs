using Microsoft.AspNetCore.Http;

namespace TaskTracker.Api.Common.Results;

/// <summary>
/// HTTP result mapping for Result and Error instances adhering strictly to RFC 9457 Problem Details.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result)
    {
        return result.IsSuccess
            ? TypedResults.NoContent()
            : MapErrorToProblemDetails(result.Error);
    }

    public static IResult ToHttpResult<TValue>(this Result<TValue> result)
    {
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : MapErrorToProblemDetails(result.Error);
    }

    public static IResult ToCreatedHttpResult<TValue>(this Result<TValue> result, string uri)
    {
        return result.IsSuccess
            ? TypedResults.Created(uri, result.Value)
            : MapErrorToProblemDetails(result.Error);
    }

    public static IResult MapErrorToProblemDetails(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
            ErrorType.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
            ErrorType.UnprocessableContent => StatusCodes.Status422UnprocessableEntity,
            ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
            ErrorType.Failure => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = error.Code
        };

        if (error.ValidationErrors is { Count: > 0 })
        {
            extensions["errors"] = error.ValidationErrors;
        }

        return Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: statusCode,
            title: GetDefaultTitleForType(error.Type),
            detail: error.Description,
            extensions: extensions);
    }

    private static string GetDefaultTitleForType(ErrorType type) => type switch
    {
        ErrorType.Validation => "Bad Request",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Not Found",
        ErrorType.Conflict => "Conflict",
        ErrorType.PreconditionFailed => "Precondition Failed",
        ErrorType.PreconditionRequired => "Precondition Required",
        ErrorType.UnprocessableContent => "Unprocessable Content",
        ErrorType.TooManyRequests => "Too Many Requests",
        ErrorType.Failure => "Internal Server Error",
        _ => "An error occurred"
    };
}

namespace TaskTracker.Api.Common.Results;

/// <summary>
/// Machine-readable categories for API and domain errors.
/// </summary>
public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    PreconditionFailed,
    PreconditionRequired,
    UnprocessableContent,
    TooManyRequests,
    Failure
}

/// <summary>
/// Represents a structured domain or application error mapped cleanly to RFC 9457 Problem Details.
/// </summary>
public sealed record Error(
    string Code,
    string Description,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static Error None => new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Validation(string code, string description, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(code, description, ErrorType.Validation, errors);

    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    public static Error PreconditionFailed(string code, string description) =>
        new(code, description, ErrorType.PreconditionFailed);

    public static Error PreconditionRequired(string code, string description) =>
        new(code, description, ErrorType.PreconditionRequired);

    public static Error UnprocessableContent(string code, string description) =>
        new(code, description, ErrorType.UnprocessableContent);

    public static Error TooManyRequests(string code, string description) =>
        new(code, description, ErrorType.TooManyRequests);

    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);
}

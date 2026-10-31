using System.Diagnostics.CodeAnalysis;

namespace TaskTracker.Api.Common.Results;

/// <summary>
/// Represents the outcome of an operation without a value payload.
/// </summary>
public readonly struct Result
{
    private readonly Error? _error;

    private Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null && error != Error.None)
        {
            throw new InvalidOperationException("Successful result cannot hold an error.");
        }

        if (!isSuccess && (error is null || error == Error.None))
        {
            throw new InvalidOperationException("Failed result must hold an error.");
        }

        IsSuccess = isSuccess;
        _error = error;
    }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error => IsFailure ? _error! : throw new InvalidOperationException("Cannot access error of a successful result.");

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Represents the outcome of an operation producing a value of type <typeparamref name="TValue"/>.
/// </summary>
public readonly struct Result<TValue>
{
    private readonly TValue? _value;
    private readonly Error? _error;

    private Result(bool isSuccess, TValue? value, Error? error)
    {
        if (isSuccess && error is not null && error != Error.None)
        {
            throw new InvalidOperationException("Successful result cannot hold an error.");
        }

        if (!isSuccess && (error is null || error == Error.None))
        {
            throw new InvalidOperationException("Failed result must hold an error.");
        }

        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public TValue Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot access value of a failed result.");

    public Error Error => IsFailure ? _error! : throw new InvalidOperationException("Cannot access error of a successful result.");

    public static Result<TValue> Success(TValue value) => new(true, value, null);

    public static Result<TValue> Failure(Error error) => new(false, default, error);

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure(error);

    public static implicit operator Result(Result<TValue> result) =>
        result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
}

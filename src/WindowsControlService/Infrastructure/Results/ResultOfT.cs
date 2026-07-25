using System.Diagnostics.CodeAnalysis;

namespace WindowsControlService.Infrastructure.Results;

/// <summary>
/// An operation that either produced a value or failed for an expected reason.
/// </summary>
/// <remarks>
/// There is deliberately no implicit conversion from <see cref="Error"/>. It makes overload
/// resolution ambiguous and hides which call produced the failure.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factories are the only way to build the struct. A non-generic "
                  + "helper would force Result.Success<T>(value) at every call site.")]
public readonly struct Result<T>
{
    private readonly T _value;

    private Result(bool isSuccess, T value, Error error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>The value produced. Reading it on a failed result is a bug, so it throws.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException(
            $"Cannot read Value of a failed Result<{typeof(T).Name}>. Error: {Error.Code} - {Error.Message}");

    public Error Error { get; }

    public static Result<T> Success(T value) => new(true, value, default);

    public static Result<T> Failure(ErrorCode code, string message) =>
        new(false, default!, new Error(code, message));

    public static Result<T> Failure(Error error) => new(false, default!, error);

    /// <summary>Lets a method body end in <c>return value;</c> instead of wrapping by hand.</summary>
    public static implicit operator Result<T>(T value) => Success(value);
}

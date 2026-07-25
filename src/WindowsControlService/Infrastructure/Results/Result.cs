namespace WindowsControlService.Infrastructure.Results;

/// <summary>An operation that either succeeded or failed for an expected reason.</summary>
public readonly struct Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, default);

    public static Result Failure(ErrorCode code, string message) => new(false, new Error(code, message));

    public static Result Failure(Error error) => new(false, error);
}

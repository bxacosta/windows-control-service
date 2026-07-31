using Microsoft.AspNetCore.Http.HttpResults;

namespace WindowsControlService.Infrastructure.Results;

/// <summary>
/// The single place where an <see cref="Error"/> becomes an HTTP response. Endpoints never
/// build a ProblemDetails of their own.
/// </summary>
public static class ErrorHttpExtensions
{
    /// <summary>
    /// Maps an error onto its status code.
    /// </summary>
    /// <remarks>
    /// The switch has no default arm on purpose: adding a value to <see cref="ErrorCode"/>
    /// without mapping it here fails the build with CS8509 instead of quietly answering 500.
    /// </remarks>
#pragma warning disable CS8524 // Unnamed enum values: a default arm here would defeat CS8509.
    public static int ToStatusCode(this Error error) => error.Code switch
    {
        ErrorCode.NotFound => StatusCodes.Status404NotFound,
        ErrorCode.Conflict => StatusCodes.Status409Conflict,
        ErrorCode.Invalid => StatusCodes.Status400BadRequest,
        ErrorCode.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCode.AccessDenied => StatusCodes.Status403Forbidden,
        ErrorCode.PlatformUnavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorCode.OperationFailed => StatusCodes.Status500InternalServerError,
    };
#pragma warning restore CS8524

    public static ProblemHttpResult ToHttpResult(this Error error) =>
        TypedResults.Problem(error.Message, statusCode: error.ToStatusCode());
}

namespace WindowsControlService.Infrastructure.Results;

/// <summary>
/// The complete set of expected failures. Anything not describable with one of these is a
/// bug and must travel as an exception, not as a <see cref="Result"/>.
/// </summary>
public enum ErrorCode
{
    NotFound,
    Conflict,
    Invalid,

    /// <summary>The process lacks the privileges the operation needs.</summary>
    AccessDenied,

    /// <summary>Code integrity tooling is missing, a registry key does not exist, an event log is unreadable.</summary>
    PlatformUnavailable,

    /// <summary>The platform operation ran and failed. Nothing changed.</summary>
    OperationFailed,
}

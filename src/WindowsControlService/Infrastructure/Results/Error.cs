using System.Diagnostics.CodeAnalysis;

namespace WindowsControlService.Infrastructure.Results;

/// <summary>
/// An expected failure. <paramref name="Message"/> is what the caller of the API is meant to
/// read: never an internal path, a stack trace or a raw platform message. Diagnostics go to
/// the log.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Error is a Visual Basic keyword. This assembly is a service, not a library "
                  + "consumed from other languages, and Error is the name the domain uses.")]
public readonly record struct Error(ErrorCode Code, string Message);

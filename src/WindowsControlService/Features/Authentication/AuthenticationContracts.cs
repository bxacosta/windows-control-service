using System.ComponentModel.DataAnnotations;

namespace WindowsControlService.Features.Authentication;

/// <param name="Initialized">Whether a password has been configured on this machine.</param>
/// <param name="Authenticated">Whether the cookie on this very request is valid.</param>
/// <param name="MinimumPasswordLength">
/// The rule the interface counts against while a password is being typed. It is the service's
/// rule and it is configurable, so the interface has to be told: a copy of the number in the
/// browser would be a second source of truth that silently stops agreeing the day it is changed.
/// Telling an unauthenticated caller the minimum length gives nothing away that trying a short
/// password would not, and the alternative is learning the rule only after being refused.
/// </param>
/// <param name="SessionTimeoutMinutes">
/// How long a session survives without activity. Shown in Settings, for the same reason: it is
/// an operational value that belongs to the service.
/// </param>
/// <param name="RequiresLettersAndDigits">
/// The other half of the password rule. Sent for the same reason as the length: the browser
/// hints while the password is being typed, and a hint it keeps a private copy of is a hint that
/// goes on being given after the rule behind it has changed.
/// </param>
public sealed record SessionResponse(
    bool Initialized,
    bool Authenticated,
    int MinimumPasswordLength,
    int SessionTimeoutMinutes,
    bool RequiresLettersAndDigits);

public sealed record ConfigurePasswordRequest([Required] string Password);

public sealed record LoginRequest([Required] string Password);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] string NewPassword);

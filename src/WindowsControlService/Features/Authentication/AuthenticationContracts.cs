using System.ComponentModel.DataAnnotations;

namespace WindowsControlService.Features.Authentication;

/// <param name="Initialized">Whether a password has been configured on this machine.</param>
/// <param name="Authenticated">Whether the cookie on this very request is valid.</param>
public sealed record SessionResponse(bool Initialized, bool Authenticated);

public sealed record ConfigurePasswordRequest([Required] string Password);

public sealed record LoginRequest([Required] string Password);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] string NewPassword);

using System.ComponentModel.DataAnnotations;

namespace WindowsControlService.Features.Authentication;

public sealed class AuthenticationOptions
{
    public const string Section = "Authentication";

    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Ten characters, and not negotiable. This machine is shared; a short password with
    /// no attempt limit falls to a loop of requests in minutes, and whoever gets in unblocks
    /// exactly what the service exists to block.
    /// </summary>
    [Range(10, 128)]
    public int MinimumPasswordLength { get; set; } = 10;

    [Range(100_000, 2_000_000)]
    public int Pbkdf2Iterations { get; set; } = 210_000;

    [Range(1, 100)]
    public int LoginAttemptsPerMinute { get; set; } = 5;
}

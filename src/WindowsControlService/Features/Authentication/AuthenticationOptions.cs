using System.ComponentModel.DataAnnotations;

namespace WindowsControlService.Features.Authentication;

public sealed class AuthenticationOptions
{
    public const string Section = "Authentication";

    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Six characters, and it is the attempt limit that makes that enough rather than the length.
    /// This machine is shared, and the threat is somebody typing at the browser, not an
    /// offline attack: whoever can read the hash already owns the machine the service protects.
    /// At <see cref="LoginAttemptsPerMinute"/> tries a minute, six characters that must mix
    /// letters and digits are out of reach of guessing by hand or by loop.
    /// </summary>
    /// <remarks>
    /// The length is configurable and the alphabet rule is not. A number can be raised without
    /// changing what "a password" means here; dropping the requirement to mix would.
    /// </remarks>
    [Range(6, 128)]
    public int MinimumPasswordLength { get; set; } = 6;

    [Range(100_000, 2_000_000)]
    public int Pbkdf2Iterations { get; set; } = 210_000;

    [Range(1, 100)]
    public int LoginAttemptsPerMinute { get; set; } = 5;
}

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.Features.Authentication;

public interface IPasswordService
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken);

    Task<Result> ConfigureAsync(string password, CancellationToken cancellationToken);

    Task<Result> ChangeAsync(string currentPassword, string newPassword, CancellationToken cancellationToken);

    /// <summary>Validates a password and hands back the current security stamp.</summary>
    Task<Result<string>> ValidateAsync(string password, CancellationToken cancellationToken);

    Task<string?> GetSecurityStampAsync(CancellationToken cancellationToken);
}

/// <remarks>
/// There is deliberately no user name anywhere. The system has no concept of users; adding one
/// would advertise flexibility that does not exist and force a comparison that never fails.
/// </remarks>
public sealed class PasswordService(
    ISettingsRepository settings,
    IOptions<AuthenticationOptions> options,
    ILogger<PasswordService> logger) : IPasswordService
{
    internal const string HashKey = "auth.password.hash";
    internal const string SaltKey = "auth.password.salt";
    internal const string IterationsKey = "auth.password.iterations";
    internal const string SecurityStampKey = "auth.security_stamp";

    private const int SaltBytes = 32;
    private const int HashBytes = 32;

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) =>
        await settings.GetAsync(HashKey, cancellationToken) is not null;

    public async Task<Result> ConfigureAsync(string password, CancellationToken cancellationToken)
    {
        if (await IsConfiguredAsync(cancellationToken))
        {
            return Result.Failure(ErrorCode.Conflict, "A password has already been configured.");
        }

        if (PolicyViolation(password) is { } violation)
        {
            return violation;
        }

        await StoreAsync(password, cancellationToken);
        logger.LogInformation("The service password was configured for the first time.");
        return Result.Success();
    }

    public async Task<Result> ChangeAsync(string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var current = await ValidateAsync(currentPassword, cancellationToken);
        if (current.IsFailure)
        {
            // Asked for on top of a valid session on purpose: this machine is shared, and
            // a browser left signed in must not be enough to take the service over.
            return Result.Failure(ErrorCode.Unauthorized, "The current password is not correct.");
        }

        if (PolicyViolation(newPassword) is { } violation)
        {
            return violation;
        }

        await StoreAsync(newPassword, cancellationToken);

        // The stamp rotated inside StoreAsync, which signs out every open session including the
        // one that made this change.
        logger.LogWarning("The service password was changed. All open sessions are now invalid.");
        return Result.Success();
    }

    public async Task<Result<string>> ValidateAsync(string password, CancellationToken cancellationToken)
    {
        // A null or blank password is a failed login, never an exception: letting it reach PBKDF2
        // surfaces as a 500 with no body, which explains nothing to anyone.
        if (string.IsNullOrEmpty(password))
        {
            return Result<string>.Failure(ErrorCode.Unauthorized, "The password is not correct.");
        }

        var storedHash = await settings.GetAsync(HashKey, cancellationToken);
        var storedSalt = await settings.GetAsync(SaltKey, cancellationToken);
        var stamp = await settings.GetAsync(SecurityStampKey, cancellationToken);

        if (storedHash is null || storedSalt is null || stamp is null)
        {
            return Result<string>.Failure(ErrorCode.Unauthorized, "The password is not correct.");
        }

        // Read from storage rather than from options, so raising the iteration count later does
        // not lock out the password already on disk.
        var iterations = int.TryParse(
            await settings.GetAsync(IterationsKey, cancellationToken),
            CultureInfo.InvariantCulture,
            out var stored)
            ? stored
            : options.Value.Pbkdf2Iterations;

        byte[] expected;
        byte[] salt;
        try
        {
            expected = Convert.FromBase64String(storedHash);
            salt = Convert.FromBase64String(storedSalt);
        }
        catch (FormatException exception)
        {
            logger.LogError(exception, "The stored password material is not valid base64.");
            return Result<string>.Failure(ErrorCode.Unauthorized, "The password is not correct.");
        }

        var candidate = Derive(password, salt, iterations);

        // Never ==: a short-circuiting comparison leaks how much of the hash matched.
        return CryptographicOperations.FixedTimeEquals(candidate, expected)
            ? Result<string>.Success(stamp)
            : Result<string>.Failure(ErrorCode.Unauthorized, "The password is not correct.");
    }

    public Task<string?> GetSecurityStampAsync(CancellationToken cancellationToken) =>
        settings.GetAsync(SecurityStampKey, cancellationToken);

    /// <summary>
    /// Length and alphabet. Case is not part of it: forcing a capital buys one bit and buys it
    /// from the person typing, who answers by capitalising the first letter.
    /// </summary>
    private Result? PolicyViolation(string? password)
    {
        var minimum = options.Value.MinimumPasswordLength;

        if (string.IsNullOrEmpty(password) || password.Length < minimum)
        {
            return Result.Failure(
                ErrorCode.Invalid,
                $"The password must be at least {minimum} characters long.");
        }

        // Both messages name the whole rule rather than the half that failed: a rule learned one
        // refusal at a time is a rule learned by being refused twice.
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return Result.Failure(
                ErrorCode.Invalid,
                $"The password must be at least {minimum} characters long and mix letters and digits.");
        }

        return null;
    }

    private async Task StoreAsync(string password, CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var iterations = options.Value.Pbkdf2Iterations;

        // One transaction. Hash and salt written separately could leave a new salt beside an old
        // hash, which locks the service permanently: nothing validates, and the first-time setup
        // refuses to run again because a hash still exists.
        await settings.SetManyAsync(
            new Dictionary<string, string>
            {
                [HashKey] = Convert.ToBase64String(Derive(password, salt, iterations)),
                [SaltKey] = Convert.ToBase64String(salt),
                [IterationsKey] = iterations.ToString(CultureInfo.InvariantCulture),
                [SecurityStampKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            },
            cancellationToken);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.Platform;

/// <inheritdoc cref="ICodeIntegrityTool"/>
public sealed class CodeIntegrityTool(
    IProcessRunner processRunner,
    IOptions<CodeIntegrityOptions> options,
    ILogger<CodeIntegrityTool> logger) : ICodeIntegrityTool
{
    /// <summary>0x80070005. CiTool reports it in the JSON body, not through the exit code.</summary>
    private const int AccessDeniedResult = -2147024891;

    private static readonly string CiToolPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "CiTool.exe");

    private static readonly string PowerShellPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    private TimeSpan Timeout => options.Value.OperationTimeout;

    public async Task<Result<PolicyState>> GetPolicyStateAsync(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);

        if (!File.Exists(CiToolPath))
        {
            return Result<PolicyState>.Failure(
                ErrorCode.PlatformUnavailable,
                "Windows code integrity tooling is not available on this machine.");
        }

        var lookup = await LookUpPolicyAsync(policyId, cancellationToken);

        // Unknown is a successful answer, not a failure of the caller. It means "do not act",
        // and the reconciliation worker needs to be able to tell it apart from "no policy".
        return lookup switch
        {
            { Queried: false } => Result<PolicyState>.Success(PolicyState.Unknown),
            { Present: true, Enforced: true } => Result<PolicyState>.Success(PolicyState.Enforced),
            _ => Result<PolicyState>.Success(PolicyState.NotEnforced),
        };
    }

    public async Task<Result> ApplyPolicyAsync(
        ReadOnlyMemory<byte> policyXml,
        CancellationToken cancellationToken = default)
    {
        if (policyXml.IsEmpty)
        {
            return Result.Failure(ErrorCode.Invalid, "The policy document is empty.");
        }

        if (!File.Exists(CiToolPath) || !File.Exists(PowerShellPath))
        {
            return Result.Failure(
                ErrorCode.PlatformUnavailable,
                "Windows code integrity tooling is not available on this machine.");
        }

        var stem = Path.Combine(Path.GetTempPath(), $"wcs-policy-{Guid.NewGuid():N}");
        var xmlPath = stem + ".xml";
        var binaryPath = stem + ".bin";

        try
        {
            await File.WriteAllBytesAsync(xmlPath, policyXml, cancellationToken);

            var converted = await ConvertToBinaryAsync(xmlPath, binaryPath, cancellationToken);
            if (converted.IsFailure)
            {
                return converted;
            }

            var update = await RunCiToolAsync(["--update-policy", binaryPath, "-json"], cancellationToken);
            return InterpretWrite(update, "apply the application blocking policy");
        }
        finally
        {
            // Always, including when something threw: a stale policy XML in the temp directory
            // is a copy of exactly which applications this machine blocks.
            DeleteQuietly(xmlPath);
            DeleteQuietly(binaryPath);
        }
    }

    public async Task<Result> RemovePolicyAsync(string policyId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);

        if (!File.Exists(CiToolPath))
        {
            return Result.Failure(
                ErrorCode.PlatformUnavailable,
                "Windows code integrity tooling is not available on this machine.");
        }

        var lookup = await LookUpPolicyAsync(policyId, cancellationToken);
        if (!lookup.Queried)
        {
            return Result.Failure(
                ErrorCode.OperationFailed,
                "The current state of the application blocking policy could not be read.");
        }

        if (!lookup.Present)
        {
            // CiTool errors when asked to remove a policy that is not installed, which is not a
            // failure from the caller's point of view. Asking first also avoids the call entirely
            // in the common case.
            return Result.Success();
        }

        var removal = await RunCiToolAsync(["--remove-policy", Braced(policyId), "-json"], cancellationToken);
        return InterpretWrite(removal, "remove the application blocking policy");
    }

    private async Task<Result> ConvertToBinaryAsync(string xmlPath, string binaryPath, CancellationToken cancellationToken)
    {
        // ConvertFrom-CIPolicy is a ConfigCI cmdlet. There is no native API for it, so the only
        // route is launching PowerShell.
        var command = string.Create(
            CultureInfo.InvariantCulture,
            $"ConvertFrom-CIPolicy -XmlFilePath '{SingleQuote(xmlPath)}' -BinaryFilePath '{SingleQuote(binaryPath)}'");

        var result = await processRunner.RunAsync(
            PowerShellPath,
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command],
            Timeout,
            cancellationToken);

        if (result.TimedOut)
        {
            logger.LogError("Converting the policy document timed out after {Timeout}.", Timeout);
            return Result.Failure(ErrorCode.OperationFailed, "Converting the policy document took too long.");
        }

        // Both checks: ConvertFrom-CIPolicy has been seen to report success without producing a
        // file, and a missing .bin fails much later with a message about CiTool instead.
        if (!result.Succeeded || !File.Exists(binaryPath))
        {
            logger.LogError(
                "ConvertFrom-CIPolicy failed with exit code {ExitCode}. stderr: {StandardError}",
                result.ExitCode,
                result.StandardError);

            return Result.Failure(ErrorCode.OperationFailed, "The policy document could not be converted.");
        }

        return Result.Success();
    }

    private async Task<ProcessResult> RunCiToolAsync(string[] arguments, CancellationToken cancellationToken) =>
        await processRunner.RunAsync(CiToolPath, arguments, Timeout, cancellationToken);

    private Result InterpretWrite(ProcessResult result, string action)
    {
        if (result.TimedOut)
        {
            logger.LogError("CiTool timed out after {Timeout} trying to {Action}.", Timeout, action);
            return Result.Failure(ErrorCode.OperationFailed, $"Windows took too long to {action}.");
        }

        var operationResult = ReadOperationResult(result.StandardOutput);

        if (result.ExitCode == AccessDeniedResult || operationResult == AccessDeniedResult)
        {
            return Result.Failure(
                ErrorCode.AccessDenied,
                $"Administrator rights are required to {action}.");
        }

        if (!result.Succeeded || (operationResult is not null && operationResult != 0))
        {
            logger.LogError(
                "CiTool failed to {Action}. Exit code {ExitCode}, OperationResult {OperationResult}. stderr: {StandardError}",
                action,
                result.ExitCode,
                operationResult,
                result.StandardError);

            return Result.Failure(ErrorCode.OperationFailed, $"Windows refused to {action}.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Reads the policy list. Every failure collapses into <c>Queried: false</c>, which is what
    /// keeps a permissions problem from reading as "no policy installed".
    /// </summary>
    private async Task<PolicyLookup> LookUpPolicyAsync(string policyId, CancellationToken cancellationToken)
    {
        var result = await RunCiToolAsync(["--list-policies", "-json"], cancellationToken);

        // The order of these checks is not negotiable. CiTool signals failure with well-formed
        // JSON that simply has no Policies array: code that only asks "is Policies missing?"
        // reports "nothing installed" every time CiTool fails, and the reconciliation worker
        // then reinstalls the policy every minute, forever.
        if (result.TimedOut || !result.Succeeded)
        {
            logger.LogWarning(
                "CiTool --list-policies failed with exit code {ExitCode}. stderr: {StandardError}",
                result.ExitCode,
                result.StandardError);

            return PolicyLookup.NotQueried;
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "CiTool --list-policies returned output that is not JSON.");
            return PolicyLookup.NotQueried;
        }

        if (root.ValueKind is not JsonValueKind.Object)
        {
            return PolicyLookup.NotQueried;
        }

        if (root.TryGetProperty("OperationResult", out var operationResult)
            && operationResult.TryGetInt32(out var code)
            && code != 0)
        {
            logger.LogWarning("CiTool --list-policies reported OperationResult {OperationResult}.", code);
            return PolicyLookup.NotQueried;
        }

        if (!root.TryGetProperty("Policies", out var policies) || policies.ValueKind is not JsonValueKind.Array)
        {
            logger.LogWarning("CiTool --list-policies returned no Policies array.");
            return PolicyLookup.NotQueried;
        }

        var wanted = Unbraced(policyId);
        foreach (var policy in policies.EnumerateArray())
        {
            if (policy.ValueKind is not JsonValueKind.Object
                || !policy.TryGetProperty("PolicyID", out var id)
                || id.ValueKind is not JsonValueKind.String)
            {
                continue;
            }

            // Ids come back without braces and in whatever case CiTool feels like.
            if (!string.Equals(Unbraced(id.GetString()), wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // IsEnforced is a JSON boolean, not a string.
            var enforced = policy.TryGetProperty("IsEnforced", out var isEnforced)
                && isEnforced.ValueKind is JsonValueKind.True;

            return new PolicyLookup(Queried: true, Present: true, Enforced: enforced);
        }

        return new PolicyLookup(Queried: true, Present: false, Enforced: false);
    }

    private static int? ReadOperationResult(string standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(standardOutput);
            return document.RootElement.ValueKind is JsonValueKind.Object
                && document.RootElement.TryGetProperty("OperationResult", out var value)
                && value.TryGetInt32(out var code)
                ? code
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Unbraced(string? policyId) => policyId?.Trim().Trim('{', '}') ?? string.Empty;

    private static string Braced(string policyId) => $"{{{Unbraced(policyId)}}}";

    private static string SingleQuote(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    private void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not delete the temporary policy file {Path}.", path);
        }
    }

    private readonly record struct PolicyLookup(bool Queried, bool Present, bool Enforced)
    {
        public static PolicyLookup NotQueried => new(false, false, false);
    }
}

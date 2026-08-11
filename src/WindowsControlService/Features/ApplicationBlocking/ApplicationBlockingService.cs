using WindowsControlService.Infrastructure.Hosting;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.ApplicationBlocking;

public interface IApplicationBlockingService
{
    Task<IReadOnlyList<BlockedApplication>> GetAllAsync(CancellationToken cancellationToken);

    Task<Result<BlockedApplication>> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<Result<long>> AddAsync(string executablePath, string name, CancellationToken cancellationToken);

    Task<Result> RemoveAsync(long id, CancellationToken cancellationToken);

    Task<Result> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken);

    Task<Result> ReconcileAsync(CancellationToken cancellationToken);

    Task<Result<PolicyStateResponse>> GetPolicyStateAsync(CancellationToken cancellationToken);
}

/// <remarks>
/// <para>
/// Every mutating operation runs inside <see cref="ISequentialExecutor"/>, and the whole block
/// does: read the database, decide, apply, write. Serialising only the CiTool call is not
/// enough -- the worker could read the list, an HTTP request could then delete the last
/// application and remove the policy, and the worker would reinstall the stale list on top.
/// </para>
/// <para>
/// The write order is the most important logic in this project. A failure must never leave the
/// machine and the database disagreeing.
/// </para>
/// </remarks>
public sealed class ApplicationBlockingService(
    IBlockedApplicationRepository repository,
    ICodeIntegrityTool codeIntegrity,
    IPortableExecutableReader executableReader,
    ISequentialExecutor executor,
    TimeProvider timeProvider,
    ILogger<ApplicationBlockingService> logger) : IApplicationBlockingService
{
    private DateTime? _lastReconciledAt;

    public Task<IReadOnlyList<BlockedApplication>> GetAllAsync(CancellationToken cancellationToken) =>
        repository.GetAllAsync(cancellationToken);

    public async Task<Result<BlockedApplication>> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(id, cancellationToken) is { } application
            ? Result<BlockedApplication>.Success(application)
            : Result<BlockedApplication>.Failure(ErrorCode.NotFound, "No blocked application with that id.");

    public Task<Result<long>> AddAsync(string executablePath, string name, CancellationToken cancellationToken) =>
        executor.RunAsync(token => AddCoreAsync(executablePath, name, token), cancellationToken);

    public Task<Result> RemoveAsync(long id, CancellationToken cancellationToken) =>
        executor.RunAsync(token => RemoveCoreAsync(id, token), cancellationToken);

    public Task<Result> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken) =>
        executor.RunAsync(token => SetEnabledCoreAsync(id, enabled, token), cancellationToken);

    public Task<Result> ReconcileAsync(CancellationToken cancellationToken) =>
        executor.RunAsync(ReconcileCoreAsync, cancellationToken);

    /// <summary>
    /// Which WDAC attribute can carry this executable's deny rule, and its value.
    /// </summary>
    /// <remarks>
    /// The order is not arbitrary. <c>FileName</c> compares against the OriginalFilename inside
    /// the binary, which is the most specific of the three; <c>InternalName</c> is nearly as
    /// specific; <c>ProductName</c> is the widest and can cover a whole suite, which is why it
    /// is last. Returns null when the binary carries none of them, and then it cannot be blocked
    /// by name at all.
    /// </remarks>
    internal static (RuleMatchField Attribute, string Value)? ResolveMatch(PeVersionFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return fields switch
        {
            { OriginalFileName: { } original } => (RuleMatchField.FileName, original),
            { InternalName: { } internalName } => (RuleMatchField.InternalName, internalName),
            { ProductName: { } product } => (RuleMatchField.ProductName, product),
            _ => null,
        };
    }

    public async Task<Result<PolicyStateResponse>> GetPolicyStateAsync(CancellationToken cancellationToken)
    {
        var state = await codeIntegrity.GetPolicyStateAsync(WdacPolicyDocument.PolicyId, cancellationToken);
        if (state.IsFailure)
        {
            return Result<PolicyStateResponse>.Failure(state.Error);
        }

        var enabled = await repository.GetEnabledAsync(cancellationToken);

        return Result<PolicyStateResponse>.Success(
            new PolicyStateResponse(state.Value.ToString(), enabled.Count, _lastReconciledAt));
    }

    private async Task<Result<long>> AddCoreAsync(string executablePath, string name, CancellationToken cancellationToken)
    {
        string fullPath;
        try
        {
            // Normalised before anything else, so C:\App\x.exe and C:\App\..\App\x.exe cannot
            // become two entries generating two rules for one executable.
            fullPath = Path.GetFullPath(executablePath);
        }
        catch (Exception exception) when (exception
            is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return Result<long>.Failure(ErrorCode.Invalid, "The executable path is not a valid Windows path.");
        }

        if (!File.Exists(fullPath))
        {
            return Result<long>.Failure(ErrorCode.Invalid, "There is no executable at that path.");
        }

        if (await repository.ExistsByPathAsync(fullPath, cancellationToken))
        {
            return Result<long>.Failure(ErrorCode.Conflict, "An entry for this executable already exists.");
        }

        var match = ResolveMatch(executableReader.ReadVersionFields(fullPath));
        if (match is null)
        {
            // No guessing. A deny rule built from the name on disk is a rule WDAC never compares
            // anything against: the policy deploys, the state reads Enforced, and the executable
            // keeps running. Refusing is the only honest answer.
            return Result<long>.Failure(
                ErrorCode.Invalid,
                "That executable carries no version information, so a rule has nothing to match "
                + "against. Blocking it would report protection that does not exist.");
        }

        var (attribute, value) = match.Value;
        var (_, productName) = executableReader.ReadDisplayInfo(fullPath);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Blocking {ExecutablePath} on {MatchAttribute}={MatchValue}.", fullPath, attribute, value);
        }

        var application = new BlockedApplication
        {
            Name = name,
            ExecutablePath = fullPath,
            MatchAttribute = attribute,
            MatchValue = value,
            ProductName = productName,
            IsEnabled = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };

        // The only operation that writes before applying, because the deny rule id is derived
        // from the row id and the policy cannot be built until the insert has happened. It is
        // compensated below.
        var id = await repository.InsertAsync(application, cancellationToken);

        var applied = await ApplyCurrentPolicyAsync(cancellationToken);
        if (applied.IsFailure)
        {
            await repository.DeleteAsync(id, cancellationToken);
            logger.LogWarning(
                "Rolled back the entry for {ExecutablePath}: the policy could not be applied.",
                fullPath);

            return Result<long>.Failure(applied.Error);
        }

        return Result<long>.Success(id);
    }

    private async Task<Result> RemoveCoreAsync(long id, CancellationToken cancellationToken)
    {
        var application = await repository.GetByIdAsync(id, cancellationToken);
        if (application is null)
        {
            return Result.Failure(ErrorCode.NotFound, "No blocked application with that id.");
        }

        // A disabled entry contributes no rule, so removing it cannot change the policy. Worth
        // the shortcut: it avoids a CiTool call that could fail for unrelated reasons.
        if (!application.IsEnabled)
        {
            await repository.DeleteAsync(id, cancellationToken);
            return Result.Success();
        }

        // Apply first, delete second. The other way round would leave a phantom block with no row
        // left to explain it.
        var applied = await ApplyPolicyForAsync(await EnabledExceptAsync(id, cancellationToken), cancellationToken);
        if (applied.IsFailure)
        {
            return applied;
        }

        await repository.DeleteAsync(id, cancellationToken);
        return Result.Success();
    }

    private async Task<Result> SetEnabledCoreAsync(long id, bool enabled, CancellationToken cancellationToken)
    {
        var application = await repository.GetByIdAsync(id, cancellationToken);
        if (application is null)
        {
            return Result.Failure(ErrorCode.NotFound, "No blocked application with that id.");
        }

        if (application.IsEnabled == enabled)
        {
            return Result.Success();
        }

        var projected = await repository.GetEnabledAsync(cancellationToken);
        var resulting = enabled
            ? [.. projected, application]
            : projected.Where(candidate => candidate.Id != id).ToList();

        // Apply the projected policy first; only touch the row if the system accepted it.
        var applied = await ApplyPolicyForAsync(resulting, cancellationToken);
        if (applied.IsFailure)
        {
            return applied;
        }

        await repository.SetEnabledAsync(id, enabled, cancellationToken);
        return Result.Success();
    }

    private async Task<Result> ReconcileCoreAsync(CancellationToken cancellationToken)
    {
        var state = await codeIntegrity.GetPolicyStateAsync(WdacPolicyDocument.PolicyId, cancellationToken);
        if (state.IsFailure)
        {
            return Result.Failure(state.Error);
        }

        _lastReconciledAt = timeProvider.GetUtcNow().UtcDateTime;

        // Unknown means CiTool could not be queried, not that there is no policy. Acting on it
        // would reinstall the policy every minute forever.
        if (state.Value is PolicyState.Unknown)
        {
            logger.LogWarning("The WDAC policy state could not be read. Skipping this reconciliation cycle.");
            return Result.Success();
        }

        var enabled = await repository.GetEnabledAsync(cancellationToken);

        return (enabled.Count, state.Value) switch
        {
            // The database is the source of truth for configuration.
            (0, PolicyState.Enforced) => await RemoveAndLogAsync(cancellationToken),
            (> 0, PolicyState.NotEnforced) => await ReapplyAndLogAsync(enabled, cancellationToken),
            _ => Result.Success(),
        };
    }

    private async Task<Result> RemoveAndLogAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning("A policy is enforced with no enabled applications. Removing it.");
        return await codeIntegrity.RemovePolicyAsync(WdacPolicyDocument.PolicyId, cancellationToken);
    }

    private async Task<Result> ReapplyAndLogAsync(
        IReadOnlyList<BlockedApplication> enabled,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "The blocking policy is not enforced but {Count} application(s) should be blocked. Reapplying.",
            enabled.Count);

        return await codeIntegrity.ApplyPolicyAsync(WdacPolicyDocument.Build(enabled), cancellationToken);
    }

    private async Task<IReadOnlyList<BlockedApplication>> EnabledExceptAsync(long id, CancellationToken cancellationToken) =>
        [.. (await repository.GetEnabledAsync(cancellationToken)).Where(application => application.Id != id)];

    private async Task<Result> ApplyCurrentPolicyAsync(CancellationToken cancellationToken) =>
        await ApplyPolicyForAsync(await repository.GetEnabledAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// An empty list means removing the policy, never applying an empty one. A service with no
    /// active blocks must not leave a policy installed.
    /// </summary>
    private Task<Result> ApplyPolicyForAsync(
        IReadOnlyList<BlockedApplication> enabled,
        CancellationToken cancellationToken) =>
        enabled.Count == 0
            ? codeIntegrity.RemovePolicyAsync(WdacPolicyDocument.PolicyId, cancellationToken)
            : codeIntegrity.ApplyPolicyAsync(WdacPolicyDocument.Build(enabled), cancellationToken);
}

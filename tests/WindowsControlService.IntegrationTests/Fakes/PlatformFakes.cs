using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Fakes;

/// <summary>
/// Hand-written doubles for the whole platform layer, not mocks from a framework: there are
/// few of them, they are explicit, and when one fails the reason reads plainly.
/// </summary>
public sealed class FakeCodeIntegrityTool : ICodeIntegrityTool
{
    public PolicyState State { get; set; } = PolicyState.NotEnforced;

    public Error? ApplyFailure { get; set; }

    public Error? RemoveFailure { get; set; }

    public List<byte[]> AppliedDocuments { get; } = [];

    public int RemoveCount { get; private set; }

    public Task<Result<PolicyState>> GetPolicyStateAsync(string policyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<PolicyState>.Success(State));

    public Task<Result> ApplyPolicyAsync(ReadOnlyMemory<byte> policyXml, CancellationToken cancellationToken = default)
    {
        if (ApplyFailure is { } failure)
        {
            return Task.FromResult(Result.Failure(failure));
        }

        AppliedDocuments.Add(policyXml.ToArray());
        State = PolicyState.Enforced;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> RemovePolicyAsync(string policyId, CancellationToken cancellationToken = default)
    {
        if (RemoveFailure is { } failure)
        {
            return Task.FromResult(Result.Failure(failure));
        }

        RemoveCount++;
        State = PolicyState.NotEnforced;
        return Task.FromResult(Result.Success());
    }
}

public sealed class FakeUsbStorageSwitch : IUsbStorageSwitch
{
    public bool Blocked { get; set; }

    /// <summary>Set to make every call answer with this error, to exercise the failure paths.</summary>
    public Error? Failure { get; set; }

    public Result<bool> IsBlocked() =>
        Failure is { } failure ? Result<bool>.Failure(failure) : Result<bool>.Success(Blocked);

    public Result Block() => Set(blocked: true);

    public Result Unblock() => Set(blocked: false);

    private Result Set(bool blocked)
    {
        if (Failure is { } failure)
        {
            return Result.Failure(failure);
        }

        Blocked = blocked;
        return Result.Success();
    }
}

public sealed class FakeProcessInventory : IProcessInventory
{
    public List<RunningApplication> Applications { get; } = [];

    public IReadOnlyList<RunningApplication> GetRunningApplications() => Applications;
}

public sealed class FakePortableExecutableReader : IPortableExecutableReader
{
    public Dictionary<string, string?> OriginalFileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, (string? FileDescription, string? ProductName)> DisplayInfo { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? ReadOriginalFileName(string executablePath) =>
        OriginalFileNames.TryGetValue(executablePath, out var name) ? name : null;

    public (string? FileDescription, string? ProductName) ReadDisplayInfo(string executablePath) =>
        DisplayInfo.TryGetValue(executablePath, out var info) ? info : (null, null);
}

public sealed class FakeLogonEventSource : ILogonEventSource
{
    public List<LogonEvent> Events { get; } = [];

    public List<TimeSpan> RequestedWindows { get; } = [];

    public IReadOnlyList<LogonEvent> Read(TimeSpan window)
    {
        RequestedWindows.Add(window);
        return Events;
    }
}

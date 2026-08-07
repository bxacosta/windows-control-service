using WindowsControlService.Infrastructure.Events;

namespace WindowsControlService.Features.ApplicationBlocking;

/// <summary>
/// Feeds the event stream with the WDAC policy state, which is the one value in this service
/// that changes without anybody asking: the reconciliation worker can find the policy gone and
/// put it back while the interface is just sitting there.
/// </summary>
public sealed class PolicyStateSnapshot(IApplicationBlockingService blocking) : IServiceEventSnapshot
{
    public const string EventName = "policy-state";

    public ValueTask<ServiceEvent?> CaptureAsync(CancellationToken cancellationToken) =>
        CaptureAsync(blocking, cancellationToken);

    /// <summary>Also used by the reconciliation worker, which publishes after every cycle.</summary>
    public static async ValueTask<ServiceEvent?> CaptureAsync(
        IApplicationBlockingService blocking,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blocking);

        var state = await blocking.GetPolicyStateAsync(cancellationToken);

        // A failure is not pushed. The stream carries state, not errors: the client that asks
        // for something gets the problem+json, and inventing an error event would give the
        // interface a second way to learn about the same failure.
        return state.IsSuccess ? new ServiceEvent(EventName, state.Value) : null;
    }
}

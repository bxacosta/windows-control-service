using WindowsControlService.Infrastructure.Events;

namespace WindowsControlService.Features.AccessHistory;

/// <summary>
/// Feeds the event stream with the size of the history. Only the count travels: the interface
/// pages the timeline itself, and pushing a page would guess which page is on screen.
/// </summary>
public sealed class AccessHistorySnapshot(IAccessHistoryService history) : IServiceEventSnapshot
{
    public const string EventName = "access-history";

    public ValueTask<ServiceEvent?> CaptureAsync(CancellationToken cancellationToken) =>
        CaptureAsync(history, cancellationToken);

    /// <summary>Also used by the ingestion worker, which publishes after a cycle that added rows.</summary>
    public static async ValueTask<ServiceEvent?> CaptureAsync(
        IAccessHistoryService history,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);

        return new ServiceEvent(EventName, new AccessHistoryTotal(await history.CountAsync(cancellationToken)));
    }
}

using WindowsControlService.Infrastructure.Events;

namespace WindowsControlService.Features.AccessHistory;

/// <param name="Total">How many events are recorded, across every origin.</param>
public sealed record AccessHistoryTotal(int Total);

/// <summary>
/// Feeds the event stream with the size of the history. Only the count travels: the interface
/// pages the timeline itself, and pushing a page would guess which page is on screen.
/// </summary>
public sealed class AccessHistorySnapshot(IAccessHistoryService history) : IServiceEventSnapshot
{
    public const string EventName = "access-history";

    public async ValueTask<ServiceEvent?> CaptureAsync(CancellationToken cancellationToken)
    {
        var page = await history.GetTimelineAsync(limit: 1, offset: 0, origin: null, cancellationToken);

        return new ServiceEvent(EventName, new AccessHistoryTotal(page.Total));
    }
}

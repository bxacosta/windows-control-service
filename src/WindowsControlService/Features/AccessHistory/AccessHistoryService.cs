using Microsoft.Extensions.Options;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.AccessHistory;

public interface IAccessHistoryService
{
    Task<int> IngestAsync(CancellationToken cancellationToken);

    Task<AccessHistoryPage> GetTimelineAsync(
        int? limit,
        int? offset,
        LogonOrigin? origin,
        CancellationToken cancellationToken);

    /// <summary>How many events are recorded, across every origin.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);
}

public sealed class AccessHistoryService(
    ILogonEventSource eventSource,
    ILogonEventRepository repository,
    IOptions<AccessHistoryOptions> options) : IAccessHistoryService
{
    /// <summary>Agreed key for events that carry no session id, so they still pair with each other.</summary>
    private const int NoSession = -1;

    public Task<int> CountAsync(CancellationToken cancellationToken) => repository.CountAsync(cancellationToken);

    public async Task<int> IngestAsync(CancellationToken cancellationToken)
    {
        // The reader never throws: an unreadable log yields an empty list and this simply
        // inserts nothing.
        var events = eventSource.Read(options.Value.IngestionWindow);

        return await repository.InsertMissingAsync(events, cancellationToken);
    }

    public async Task<AccessHistoryPage> GetTimelineAsync(
        int? limit,
        int? offset,
        LogonOrigin? origin,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(limit ?? options.Value.DefaultPageSize, 1, options.Value.MaxPageSize);
        var skip = Math.Max(offset ?? 0, 0);

        var timeline = BuildTimeline(await repository.GetAllAscendingAsync(cancellationToken));

        // Filtered after deriving, never before: a logoff with no Address of its own has to be
        // able to match "remote" through the origin it inherited from its own session start.
        var filtered = origin is { } wanted
            ? timeline.Where(entry => entry.Origin == wanted).ToList()
            : timeline;

        // Total counts what matches the filter, and is taken before paging.
        var total = filtered.Count;

        var entries = filtered
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToList();

        return new AccessHistoryPage(entries, total);
    }

    /// <summary>
    /// Walks the events oldest first, pairing each session end with the start of its own session.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, because a duration and an inherited origin are relations
    /// between events, and that relation changes as new events arrive. Storing them would be
    /// storing a conclusion with an expiry date.
    /// </remarks>
    internal List<AccessHistoryEntry> BuildTimeline(IReadOnlyList<StoredLogonEvent> ascending)
    {
        // Keyed by session. One global "last start" would mix the durations of two concurrent
        // sessions together.
        var lastStart = new Dictionary<int, DateTime>();
        var lastKnown = new Dictionary<int, (LogonOrigin Origin, string? Address)>();
        var result = new List<AccessHistoryEntry>(ascending.Count);

        foreach (var stored in ascending)
        {
            var logonEvent = stored.Event;
            var session = logonEvent.SessionId ?? NoSession;
            var origin = logonEvent.Origin;
            var address = logonEvent.Address;

            if (logonEvent.IsSessionStart)
            {
                lastStart[session] = logonEvent.OccurredAt;
                lastKnown[session] = (origin, address);

                result.Add(Entry(stored, origin, address, durationSeconds: null));
                continue;
            }

            // Event 23 carries no Address at all, so a session end inherits what its own start
            // knew.
            if (origin is LogonOrigin.Unknown && lastKnown.TryGetValue(session, out var known))
            {
                (origin, address) = known;
            }

            int? durationSeconds = null;
            if (lastStart.TryGetValue(session, out var startedAt))
            {
                var elapsed = logonEvent.OccurredAt - startedAt;
                if (elapsed > TimeSpan.Zero && elapsed <= options.Value.MaxPlausibleSessionLength)
                {
                    durationSeconds = (int)elapsed.TotalSeconds;
                }

                // Removed once paired, so a second close of the same session cannot reuse the
                // same start and report a duration twice.
                lastStart.Remove(session);
            }

            result.Add(Entry(stored, origin, address, durationSeconds));
        }

        return result;
    }

    private static AccessHistoryEntry Entry(
        StoredLogonEvent stored,
        LogonOrigin origin,
        string? address,
        int? durationSeconds) =>
        new(
            stored.Id,
            stored.Event.OccurredAt,
            stored.Event.Kind,
            stored.Event.IsSessionStart,
            origin,
            address,
            stored.Event.UserName,
            stored.Event.SessionId,
            durationSeconds);
}

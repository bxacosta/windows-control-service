using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.AccessHistory;

public sealed class AccessHistoryOptions
{
    public const string Section = "AccessHistory";

    public TimeSpan IngestionInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How far back each cycle re-reads. There is no watermark; see the worker.</summary>
    public TimeSpan IngestionWindow { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Beyond this, a computed session length is treated as nonsense and reported as unknown.
    /// An absurd interval almost always means the real start fell outside the window.
    /// </summary>
    public TimeSpan MaxPlausibleSessionLength { get; set; } = TimeSpan.FromDays(7);

    [Range(1, 500)]
    public int DefaultPageSize { get; set; } = 10;

    [Range(1, 5000)]
    public int MaxPageSize { get; set; } = 500;
}

/// <param name="StartsSession">
/// Whether this event opens a session rather than closing one. Sent rather than left for the
/// client to work out from <paramref name="Kind"/>, because which event ids begin a session is a
/// fact about Windows and this service already owns it: <see cref="LogonEvent.IsSessionStart"/>
/// is what pairs an end with its start to produce <paramref name="DurationSeconds"/>. A client
/// deriving the same rule would be a second copy of it, and a copy that reads
/// <c>Kind == Logon</c> is exactly the copy that got written -- mislabelling every Reconnect on
/// a machine where Reconnect is half of all traffic.
/// </param>
/// <param name="DurationSeconds">
/// Only ever set on an entry that ends a session, and null when the matching start fell outside
/// the window or the interval was not plausible.
/// </param>
public sealed record AccessHistoryEntry(
    long Id,
    DateTime OccurredAt,
    LogonEventKind Kind,
    bool StartsSession,
    LogonOrigin Origin,
    string? Address,
    string UserName,
    int? SessionId,
    int? DurationSeconds);

/// <param name="Total">
/// How many entries match the current filter, not how many were returned. It is what tells a
/// client how many pages exist.
/// </param>
public sealed record AccessHistoryPage(IReadOnlyList<AccessHistoryEntry> Entries, int Total);

public interface IAccessHistoryService
{
    Task<int> IngestAsync(CancellationToken cancellationToken);

    Task<AccessHistoryPage> GetTimelineAsync(
        int? limit,
        int? offset,
        LogonOrigin? origin,
        CancellationToken cancellationToken);
}

public sealed class AccessHistoryService(
    ILogonEventSource eventSource,
    ILogonEventRepository repository,
    IOptions<AccessHistoryOptions> options) : IAccessHistoryService
{
    /// <summary>Agreed key for events that carry no session id, so they still pair with each other.</summary>
    private const int NoSession = -1;

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

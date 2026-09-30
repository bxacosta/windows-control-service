using WindowsControlService.Platform;

namespace WindowsControlService.Features.AccessHistory;

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

/// <param name="Total">How many events are recorded, across every origin.</param>
public sealed record AccessHistoryTotal(int Total);

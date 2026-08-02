using WindowsControlService.Features.AccessHistory;
using WindowsControlService.Platform;

namespace WindowsControlService.UnitTests.Fakes;

/// <summary>
/// In-memory stand-in that honours the same (Channel, RecordId, OccurredAt) uniqueness the real
/// table does, so the idempotence of ingestion is really being tested rather than assumed.
/// </summary>
internal sealed class FakeLogonEventRepository : ILogonEventRepository
{
    public List<StoredLogonEvent> Stored { get; } = [];

    public Task<int> InsertMissingAsync(IEnumerable<LogonEvent> events, CancellationToken cancellationToken)
    {
        var inserted = 0;

        foreach (var candidate in events)
        {
            var exists = Stored.Any(stored =>
                string.Equals(stored.Event.Channel, candidate.Channel, StringComparison.Ordinal)
                && stored.Event.RecordId == candidate.RecordId
                && stored.Event.OccurredAt == candidate.OccurredAt);

            if (exists)
            {
                continue;
            }

            Stored.Add(new StoredLogonEvent(Stored.Count + 1, candidate));
            inserted++;
        }

        return Task.FromResult(inserted);
    }

    public Task<IReadOnlyList<StoredLogonEvent>> GetAllAscendingAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredLogonEvent>>(
            [.. Stored.OrderBy(stored => stored.Event.OccurredAt).ThenBy(stored => stored.Id)]);

    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Stored.Count);
}

internal sealed class FakeLogonEventSource : ILogonEventSource
{
    public List<LogonEvent> Events { get; } = [];

    public List<TimeSpan> RequestedWindows { get; } = [];

    public IReadOnlyList<LogonEvent> Read(TimeSpan window)
    {
        RequestedWindows.Add(window);
        return Events;
    }
}

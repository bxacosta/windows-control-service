using Microsoft.Extensions.Options;
using WindowsControlService.Features.AccessHistory;
using WindowsControlService.Platform;
using WindowsControlService.UnitTests.Fakes;

namespace WindowsControlService.UnitTests.Features.AccessHistory;

/// <summary>
/// The pairing logic, driven entirely through doubles. Durations and inherited origins are
/// relations between events, so every case here is about which neighbour an event pairs with.
/// </summary>
public sealed class AccessHistoryTimelineTests
{
    private static readonly DateTime Base = new(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeLogonEventSource _source = new();
    private readonly FakeLogonEventRepository _repository = new();
    private readonly AccessHistoryOptions _options = new();

    private AccessHistoryService Service => new(_source, _repository, Options.Create(_options));

    [Fact]
    public async Task AStartFollowedByAnEndCarriesTheDurationOnTheEnd()
    {
        Store(1, Base, LogonEventKind.Logon, session: 2, address: "LOCAL");
        Store(2, Base.AddMinutes(44), LogonEventKind.Logoff, session: 2);

        var page = await Service.GetTimelineAsync(null, null, null, CancellationToken.None);

        Assert.Equal(2640, Entry(page, 2).DurationSeconds);
        Assert.Null(Entry(page, 1).DurationSeconds);
    }

    [Fact]
    public async Task AnEndWithNoStartInTheWindowHasNoDuration()
    {
        Store(1, Base, LogonEventKind.Logoff, session: 2);

        var page = await Service.GetTimelineAsync(null, null, null, CancellationToken.None);

        // Reporting one would be inventing it.
        Assert.Null(Entry(page, 1).DurationSeconds);
    }

    [Fact]
    public async Task AnImplausiblyLongIntervalIsReportedAsUnknown()
    {
        Store(1, Base, LogonEventKind.Logon, session: 2, address: "LOCAL");
        Store(2, Base.AddDays(9), LogonEventKind.Logoff, session: 2);

        var page = await Service.GetTimelineAsync(null, null, null, CancellationToken.None);

        // Nine days past a seven day maximum almost always means the real start fell outside
        // the window and this end paired with something older.
        Assert.Null(Entry(page, 2).DurationSeconds);
    }

    [Fact]
    public async Task AnEndWithNoAddressInheritsOriginAndAddressFromItsOwnStart()
    {
        Store(1, Base, LogonEventKind.Logon, session: 3, address: "203.0.113.2");
        Store(2, Base.AddMinutes(10), LogonEventKind.Logoff, session: 3);

        var page = await Service.GetTimelineAsync(null, null, null, CancellationToken.None);

        // Event 23 carries no Address element at all.
        Assert.Equal(LogonOrigin.Remote, Entry(page, 2).Origin);
        Assert.Equal("203.0.113.2", Entry(page, 2).Address);
    }

    [Fact]
    public async Task TwoInterleavedSessionsEachPairWithTheirOwnStart()
    {
        Store(1, Base, LogonEventKind.Logon, session: 1, address: "LOCAL");
        Store(2, Base.AddMinutes(5), LogonEventKind.Logon, session: 2, address: "203.0.113.9");
        Store(3, Base.AddMinutes(20), LogonEventKind.Logoff, session: 1);
        Store(4, Base.AddMinutes(65), LogonEventKind.Logoff, session: 2);

        var page = await Service.GetTimelineAsync(null, null, null, CancellationToken.None);

        // A single global "last start" would give both ends the same wrong answer.
        Assert.Equal(20 * 60, Entry(page, 3).DurationSeconds);
        Assert.Equal(60 * 60, Entry(page, 4).DurationSeconds);
        Assert.Equal(LogonOrigin.Local, Entry(page, 3).Origin);
        Assert.Equal(LogonOrigin.Remote, Entry(page, 4).Origin);
    }

    [Fact]
    public async Task ASecondEndForTheSameSessionDoesNotReuseTheStart()
    {
        Store(1, Base, LogonEventKind.Logon, session: 1, address: "LOCAL");
        Store(2, Base.AddMinutes(10), LogonEventKind.Disconnect, session: 1);
        Store(3, Base.AddMinutes(30), LogonEventKind.Logoff, session: 1);

        var page = await Service.GetTimelineAsync(null, null, null, CancellationToken.None);

        Assert.Equal(600, Entry(page, 2).DurationSeconds);
        Assert.Null(Entry(page, 3).DurationSeconds);
    }

    [Fact]
    public async Task TheRemoteFilterIncludesAnEndThatOnlyInheritedItsOrigin()
    {
        Store(1, Base, LogonEventKind.Logon, session: 3, address: "203.0.113.2");
        Store(2, Base.AddMinutes(10), LogonEventKind.Logoff, session: 3);
        Store(3, Base.AddMinutes(20), LogonEventKind.Logon, session: 4, address: "LOCAL");

        var page = await Service.GetTimelineAsync(null, null, LogonOrigin.Remote, CancellationToken.None);

        // Filtering before deriving would lose the logoff, because on its own it has no address.
        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Entries.Count);
        Assert.All(page.Entries, entry => Assert.Equal(LogonOrigin.Remote, entry.Origin));
    }

    [Fact]
    public async Task TheLocalFilterLeavesTheRemoteOnesOut()
    {
        Store(1, Base, LogonEventKind.Logon, session: 3, address: "203.0.113.2");
        Store(2, Base.AddMinutes(20), LogonEventKind.Logon, session: 4, address: "LOCAL");

        var page = await Service.GetTimelineAsync(null, null, LogonOrigin.Local, CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal(2, Assert.Single(page.Entries).Id);
    }

    [Fact]
    public async Task PagesAreNewestFirstAndDoNotOverlap()
    {
        for (var i = 1; i <= 25; i++)
        {
            Store(i, Base.AddMinutes(i), LogonEventKind.Logon, session: i, address: "LOCAL");
        }

        var first = await Service.GetTimelineAsync(10, 0, null, CancellationToken.None);
        var second = await Service.GetTimelineAsync(10, 10, null, CancellationToken.None);

        Assert.Equal(25, first.Total);
        Assert.Equal(25, second.Total);
        Assert.Equal(25, first.Entries[0].Id);
        Assert.Equal(16, first.Entries[^1].Id);
        Assert.Equal(15, second.Entries[0].Id);
        Assert.Empty(first.Entries.Select(e => e.Id).Intersect(second.Entries.Select(e => e.Id)));
    }

    [Fact]
    public async Task TheLimitIsClampedAndANegativeOffsetIsTreatedAsZero()
    {
        for (var i = 1; i <= 5; i++)
        {
            Store(i, Base.AddMinutes(i), LogonEventKind.Logon, session: i, address: "LOCAL");
        }

        Assert.Single((await Service.GetTimelineAsync(0, null, null, CancellationToken.None)).Entries);
        Assert.Equal(5, (await Service.GetTimelineAsync(9999, null, null, CancellationToken.None)).Entries.Count);
        Assert.Equal(5, (await Service.GetTimelineAsync(10, -5, null, CancellationToken.None)).Entries[0].Id);
    }

    [Fact]
    public async Task IngestingTwiceInsertsNothingTheSecondTime()
    {
        _source.Events.Add(Event(1, Base, LogonEventKind.Logon, session: 1, address: "LOCAL"));

        Assert.Equal(1, await Service.IngestAsync(CancellationToken.None));
        Assert.Equal(0, await Service.IngestAsync(CancellationToken.None));
        Assert.Equal(1, await _repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IngestionAsksForExactlyTheConfiguredWindow()
    {
        _options.IngestionWindow = TimeSpan.FromDays(14);

        await Service.IngestAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromDays(14), Assert.Single(_source.RequestedWindows));
    }

    [Fact]
    public async Task AnUnreadableLogIngestsNothingWithoutThrowing()
    {
        // The reader never throws; it returns an empty list. This is the other half of that
        // contract: ingestion has to cope with it quietly.
        Assert.Equal(0, await Service.IngestAsync(CancellationToken.None));
        Assert.Equal(0, await _repository.CountAsync(CancellationToken.None));
    }

    private static AccessHistoryEntry Entry(AccessHistoryPage page, long id) =>
        page.Entries.Single(entry => entry.Id == id);

    private void Store(long id, DateTime occurredAt, LogonEventKind kind, int? session, string? address = null) =>
        _repository.Stored.Add(new StoredLogonEvent(id, Event(id, occurredAt, kind, session, address)));

    private static LogonEvent Event(long recordId, DateTime occurredAt, LogonEventKind kind, int? session, string? address) =>
        new(
            Channel: "test",
            RecordId: recordId,
            EventId: kind switch
            {
                LogonEventKind.Logon => 21,
                LogonEventKind.Logoff => 23,
                LogonEventKind.Disconnect => 24,
                _ => 25,
            },
            Kind: kind,
            OccurredAt: occurredAt,
            UserName: @"MACHINE\owner",
            SessionId: session,
            Address: address,
            Origin: LogonEventSource.ToOrigin(address));
}

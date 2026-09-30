using WindowsControlService.Features.AccessHistory;
using WindowsControlService.IntegrationTests.Infrastructure.Database;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Features.AccessHistory;

/// <summary>
/// The real SQL behind ingestion. Every cycle re-reads the whole window and relies on this table
/// ignoring what it already has; the unit tests' fake assumes that contract, and this is where it
/// is held.
/// </summary>
public sealed class LogonEventRepositoryTests : IDisposable
{
    private const string Channel = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational";

    private static readonly DateTime Noon = new(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly MigratedDatabase _database = new(services =>
        services.AddSingleton<ILogonEventRepository, LogonEventRepository>());

    private ILogonEventRepository Repository => _database.Get<ILogonEventRepository>();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ReadingTheSameWindowTwiceStoresEachEventOnce()
    {
        LogonEvent[] window = [Event(1, Noon), Event(2, Noon.AddMinutes(5))];

        var first = await Repository.InsertMissingAsync(window, CancellationToken.None);
        var second = await Repository.InsertMissingAsync(window, CancellationToken.None);

        Assert.Equal(2, first);
        Assert.Equal(0, second);
        Assert.Equal(2, await Repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ARecordIdReusedAtAnotherTimeIsAnotherEvent()
    {
        // Record ids restart when the log is cleared, so the key includes the time: a reused id
        // must not swallow a new event as a duplicate of an old one.
        await Repository.InsertMissingAsync([Event(1, Noon)], CancellationToken.None);

        var inserted = await Repository.InsertMissingAsync([Event(1, Noon.AddDays(3))], CancellationToken.None);

        Assert.Equal(1, inserted);
    }

    [Fact]
    public async Task EventsComeBackOldestFirstAndInUtc()
    {
        await Repository.InsertMissingAsync(
            [Event(3, Noon.AddHours(2)), Event(1, Noon), Event(2, Noon.AddHours(1))],
            CancellationToken.None);

        var stored = await Repository.GetAllAscendingAsync(CancellationToken.None);

        Assert.Equal([Noon, Noon.AddHours(1), Noon.AddHours(2)], stored.Select(entry => entry.Event.OccurredAt));
        Assert.All(stored, entry => Assert.Equal(DateTimeKind.Utc, entry.Event.OccurredAt.Kind));
    }

    private static LogonEvent Event(long recordId, DateTime occurredAt) =>
        new(Channel, recordId, 21, LogonEventKind.Logon, occurredAt, @"MACHINE\owner", 2, "LOCAL", LogonOrigin.Local);
}

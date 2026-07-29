using Microsoft.Extensions.Logging.Abstractions;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Platform;

/// <summary>
/// Reads the machine's real Terminal Services log. Read-only, but it needs the rights to open
/// that channel.
/// </summary>
[Trait("Requires", "Admin")]
public sealed class LogonEventSourceTests
{
    private readonly LogonEventSource _source = new(NullLogger<LogonEventSource>.Instance);

    [Fact]
    public void ReadsEventsFromTheRealLog()
    {
        var events = _source.Read(TimeSpan.FromDays(30));

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.NotEqual(default, e.OccurredAt));
        Assert.All(events, e => Assert.Equal(DateTimeKind.Utc, e.OccurredAt.Kind));
        Assert.Contains(events, e => e.IsSessionStart);
    }

    [Fact]
    public void EveryEventCarriesTheFieldsTheHistoryNeeds()
    {
        var events = _source.Read(TimeSpan.FromDays(30));

        Assert.All(events, e =>
        {
            Assert.Equal(LogonEventSource.DefaultChannel, e.Channel);
            Assert.True(e.RecordId > 0);
            Assert.Contains(e.EventId, (int[])[21, 23, 24, 25]);
            Assert.False(string.IsNullOrWhiteSpace(e.UserName));
        });
    }

    [Fact]
    public void SessionEndEventsParseEvenWithoutAnAddress()
    {
        var events = _source.Read(TimeSpan.FromDays(30));
        var ends = events.Where(e => e.Kind is LogonEventKind.Logoff).ToList();

        Assert.NotEmpty(ends);

        // Event 23 carries no Address element. Positional parsing breaks exactly here, so these
        // records have to come back complete apart from that one field.
        Assert.All(ends, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.UserName));
            Assert.Equal(LogonOrigin.Unknown, e.Origin);
            Assert.Null(e.Address);
        });
    }

    [Fact]
    public void TheWindowIsRespected()
    {
        var wide = _source.Read(TimeSpan.FromDays(30));
        var narrow = _source.Read(TimeSpan.FromMinutes(1));

        Assert.True(narrow.Count <= wide.Count);
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        Assert.All(narrow, e => Assert.True(e.OccurredAt >= cutoff));
    }

    [Fact]
    public void AChannelThatDoesNotExistYieldsAnEmptyListInsteadOfAnException()
    {
        // An unreadable log must never take the ingestion down with it.
        var missing = new LogonEventSource(
            NullLogger<LogonEventSource>.Instance,
            "Microsoft-Windows-NoSuchChannel/Operational");

        Assert.Empty(missing.Read(TimeSpan.FromDays(30)));
    }

    [Theory]
    [InlineData(null, LogonOrigin.Unknown)]
    [InlineData("", LogonOrigin.Unknown)]
    [InlineData("   ", LogonOrigin.Unknown)]
    [InlineData("LOCAL", LogonOrigin.Local)]
    [InlineData("local", LogonOrigin.Local)]
    [InlineData("203.0.113.40", LogonOrigin.Remote)]
    [InlineData("fe80::1", LogonOrigin.Remote)]
    public void OriginIsClassifiedFromTheAddress(string? address, LogonOrigin expected)
    {
        Assert.Equal(expected, LogonEventSource.ToOrigin(address));
    }
}

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

    /// <summary>
    /// The events this machine actually has, from the narrowest of a few windows that contains
    /// any of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fixed 30 day window made every test below a statement about how the machine had been
    /// used rather than about the parser. Measured on the machine this was written on: the
    /// channel held 569 records inside 30 days and not one of them was a session event -- all
    /// of them were id 59, session arbitration -- because the machine is never signed out of and
    /// never reached over RDP. The newest logon and logoff were 45 days old. So every assertion
    /// here was made against an empty list: two failed outright, and the two written with
    /// <c>Assert.All</c> passed while proving nothing, which is worse.
    /// </para>
    /// <para>
    /// Widening the fixed window to 180 days would move the same failure 150 days out. This asks
    /// the reader itself for progressively wider windows and stops at the first that returns
    /// anything, so the parser is exercised against real records whenever the machine has ever
    /// produced any, and none of it depends on when it last did.
    /// </para>
    /// <para>
    /// The escalation goes through <see cref="LogonEventSource.Read"/> rather than querying the
    /// channel directly, so this file keeps no second copy of which channel and which event ids
    /// a session is made of.
    /// </para>
    /// </remarks>
    private IReadOnlyList<LogonEvent> RecordedSessions()
    {
        foreach (var days in (int[])[30, 180, 730])
        {
            var events = _source.Read(TimeSpan.FromDays(days));
            if (events.Count > 0)
            {
                return events;
            }
        }

        return [];
    }

    /// <summary>
    /// xunit 2.9 has no dynamic skip, so a machine with nothing to parse has to fail rather than
    /// pass quietly. The message is what makes that useful: it says the machine is the reason,
    /// not the code, and what to do about it.
    /// </summary>
    private static void RequireRecordedSessions(IReadOnlyList<LogonEvent> events) =>
        Assert.True(
            events.Count > 0,
            "This machine has recorded no logon, logoff, disconnect or reconnect in "
            + $"{LogonEventSource.DefaultChannel} within two years, so the parser cannot be "
            + "exercised against real records here. This is a fact about the machine, not a "
            + "failure of the reader: sign out and back in, or connect once over Remote Desktop, "
            + "and run it again.");

    [Fact]
    public void ReadsEventsFromTheRealLog()
    {
        var events = RecordedSessions();

        RequireRecordedSessions(events);
        Assert.All(events, e => Assert.NotEqual(default, e.OccurredAt));
        Assert.All(events, e => Assert.Equal(DateTimeKind.Utc, e.OccurredAt.Kind));
        Assert.Contains(events, e => e.IsSessionStart);
    }

    [Fact]
    public void EveryEventCarriesTheFieldsTheHistoryNeeds()
    {
        var events = RecordedSessions();

        RequireRecordedSessions(events);
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
        var ends = RecordedSessions().Where(e => e.Kind is LogonEventKind.Logoff).ToList();

        Assert.True(
            ends.Count > 0,
            "No logoff (event 23) was found within two years, so the one record shape that has no "
            + "Address element is not being parsed by this test on this machine.");

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
        // Deliberately not the calibrated window: what this proves is that a narrower window
        // cannot return more than a wider one, and that holds whether or not either has records.
        var wide = _source.Read(TimeSpan.FromDays(730));
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

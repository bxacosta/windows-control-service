using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Features.AccessHistory;

public sealed class AccessHistoryHttpTests : IDisposable
{
    private const string Password = "una-contrasena-larga";
    private static readonly DateTime Base = new(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);

    private readonly ServiceApplicationFactory _factory;

    public AccessHistoryHttpTests()
    {
        _factory = new ServiceApplicationFactory()
            .WithGenerousLoginLimit()
            .With("ApplicationBlocking:ReconciliationInterval", "01:00:00")
            // Long enough that the ingestion worker runs exactly once, at startup, and does not
            // re-ingest underneath an assertion.
            .With("AccessHistory:IngestionInterval", "01:00:00");
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task TheHistoryRequiresASession()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/access-history", CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task TheWorkerIngestsAtStartup()
    {
        Seed(25);
        using var client = await SignedInClientAsync();

        var page = await WaitForEntriesAsync(client, "/api/access-history?limit=50");

        // Nothing in this test asked for ingestion: the worker did it on start.
        Assert.Equal(25, page.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task PagesDoNotOverlapAndShareTheSameTotal()
    {
        Seed(25);
        using var client = await SignedInClientAsync();
        await WaitForEntriesAsync(client, "/api/access-history?limit=10&offset=0");

        var first = await client.GetFromJsonAsync<JsonElement>("/api/access-history?limit=10&offset=0", CancellationToken.None);
        var second = await client.GetFromJsonAsync<JsonElement>("/api/access-history?limit=10&offset=10", CancellationToken.None);

        Assert.Equal(25, first.GetProperty("total").GetInt32());
        Assert.Equal(25, second.GetProperty("total").GetInt32());

        var firstIds = Ids(first);
        var secondIds = Ids(second);

        Assert.Equal(10, firstIds.Count);
        Assert.Equal(10, secondIds.Count);
        Assert.Empty(firstIds.Intersect(secondIds));
    }

    [Fact]
    public async Task EntriesComeBackNewestFirstAndInUtc()
    {
        Seed(5);
        using var client = await SignedInClientAsync();

        var page = await WaitForEntriesAsync(client, "/api/access-history?limit=5");
        var occurred = page.GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("occurredAt").GetString()!)
            .ToList();

        Assert.All(occurred, value => Assert.EndsWith("Z", value, StringComparison.Ordinal));
        Assert.Equal(occurred.OrderByDescending(value => value, StringComparer.Ordinal), occurred);

        // Names, not the integers behind the enums: the contract says "Logon" and "Local".
        var first = page.GetProperty("entries").EnumerateArray().First();
        Assert.Equal("Logon", first.GetProperty("kind").GetString());
        Assert.Equal("Local", first.GetProperty("origin").GetString());
    }

    [Fact]
    public async Task EveryEntrySaysWhetherItOpensASessionOrClosesOne()
    {
        // Reconnect and Disconnect, because they are what a real machine actually records:
        // 456 of the 474 relevant entries in the channel. A client left to work the direction out
        // from the kind reads "Logon" and calls the other three an ending.
        _factory.LogonEvents.Events.Add(Event(1, Base, LogonEventKind.Logon, session: 1, address: "203.0.113.2"));
        _factory.LogonEvents.Events.Add(Event(2, Base.AddMinutes(5), LogonEventKind.Disconnect, session: 1));
        _factory.LogonEvents.Events.Add(Event(3, Base.AddMinutes(10), LogonEventKind.Reconnect, session: 1, address: "203.0.113.2"));
        _factory.LogonEvents.Events.Add(Event(4, Base.AddMinutes(20), LogonEventKind.Logoff, session: 1));

        using var client = await SignedInClientAsync();
        var page = await WaitForEntriesAsync(client, "/api/access-history");

        var byKind = page.GetProperty("entries").EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("kind").GetString()!,
                entry => entry.GetProperty("startsSession").GetBoolean());

        Assert.True(byKind["Logon"]);
        Assert.True(byKind["Reconnect"]);
        Assert.False(byKind["Disconnect"]);
        Assert.False(byKind["Logoff"]);

        // The same rule that answers the field is the one that pairs a duration, so a Reconnect
        // has to be the start the following Logoff is measured from: 10 minutes, not 20.
        var logoff = page.GetProperty("entries").EnumerateArray()
            .First(entry => entry.GetProperty("kind").GetString() == "Logoff");

        Assert.Equal(600, logoff.GetProperty("durationSeconds").GetInt32());
    }

    [Fact]
    public async Task TheRemoteFilterNarrowsTheTotal()
    {
        _factory.LogonEvents.Events.Add(Event(1, Base, LogonEventKind.Logon, session: 1, address: "203.0.113.2"));
        _factory.LogonEvents.Events.Add(Event(2, Base.AddMinutes(5), LogonEventKind.Logoff, session: 1));
        _factory.LogonEvents.Events.Add(Event(3, Base.AddMinutes(10), LogonEventKind.Logon, session: 2, address: "LOCAL"));

        using var client = await SignedInClientAsync();
        await WaitForEntriesAsync(client, "/api/access-history");

        var remote = await client.GetFromJsonAsync<JsonElement>("/api/access-history?origin=remote", CancellationToken.None);
        var local = await client.GetFromJsonAsync<JsonElement>("/api/access-history?origin=LOCAL", CancellationToken.None);
        var all = await client.GetFromJsonAsync<JsonElement>("/api/access-history?origin=all", CancellationToken.None);

        // The logoff has no address of its own and still counts as remote, through the origin it
        // inherited.
        Assert.Equal(2, remote.GetProperty("total").GetInt32());
        Assert.Equal(1, local.GetProperty("total").GetInt32());
        Assert.Equal(3, all.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("noexiste")]
    [InlineData("unknown")]
    public async Task AnInvalidOriginIsABadRequest(string origin)
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync($"/api/access-history?origin={origin}", CancellationToken.None);

        // Unknown is an internal state, not something a caller gets to ask for.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TheProcessListIsServedFromTheInventory()
    {
        _factory.ProcessInventory.Applications.Add(
            new RunningApplication("Editor", @"D:\Apps\editor.exe", "Editor Suite"));

        using var client = await SignedInClientAsync();

        var processes = await client.GetFromJsonAsync<JsonElement>("/api/processes", CancellationToken.None);
        var only = processes.EnumerateArray().Single();

        Assert.Equal("Editor", only.GetProperty("name").GetString());
        Assert.Equal(@"D:\Apps\editor.exe", only.GetProperty("executablePath").GetString());
    }

    private void Seed(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            _factory.LogonEvents.Events.Add(
                Event(i, Base.AddMinutes(i), LogonEventKind.Logon, session: i, address: "LOCAL"));
        }
    }

    private static LogonEvent Event(long recordId, DateTime occurredAt, LogonEventKind kind, int? session, string? address = null) =>
        new(
            Channel: LogonEventSource.DefaultChannel,
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

    private static List<long> Ids(JsonElement page) =>
        [.. page.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("id").GetInt64())];

    /// <summary>Polls until the background worker has ingested, rather than sleeping and hoping.</summary>
    private static async Task<JsonElement> WaitForEntriesAsync(HttpClient client, string url)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var page = await client.GetFromJsonAsync<JsonElement>(url, CancellationToken.None);
            if (page.GetProperty("total").GetInt32() > 0)
            {
                return page;
            }

            await Task.Delay(50);
        }

        Assert.Fail("the ingestion worker never produced any entries");
        return default;
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/password", new { password = Password }, CancellationToken.None);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return client;
    }
}

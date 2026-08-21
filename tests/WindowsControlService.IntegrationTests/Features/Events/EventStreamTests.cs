using System.Net;
using System.Net.Http.Json;

namespace WindowsControlService.IntegrationTests.Features.Events;

/// <summary>
/// The event stream, driven over real HTTP. The lifetime is turned down to a couple of seconds
/// so the tests can read a stream to its end instead of racing a timeout: that the stream ends
/// by itself is the behaviour the sliding session cookie depends on, so measuring it here is the
/// point rather than a convenience.
/// </summary>
public sealed class EventStreamTests : IDisposable
{
    private const string Password = "a-long-test-password-2026";

    private readonly ServiceApplicationFactory _factory = new ServiceApplicationFactory()
        .WithGenerousLoginLimit()
        .With("ApplicationBlocking:ReconciliationInterval", "01:00:00")
        .With("Events:StreamLifetime", "00:00:02");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task TheStreamRequiresASession()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/events", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConnectingDeliversTheCurrentStateOfEverything()
    {
        using var client = await SignedInClientAsync();

        using var response = await client.GetAsync("/api/events", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        // One connection answers what three GETs would have.
        Assert.Contains("event: policy-state", body, StringComparison.Ordinal);
        Assert.Contains("event: usb", body, StringComparison.Ordinal);
        Assert.Contains("event: access-history", body, StringComparison.Ordinal);

        // The enum goes out as a name. This is the second of the two exits the same record takes:
        // the stream does not serialize with the REST pipeline, so "the application's JSON
        // options apply here too" is checked rather than assumed. As an integer the interface
        // could not tell Unknown from NotEnforced without knowing the member order, and those
        // two mean opposite things.
        Assert.Contains("\"state\":\"NotEnforced\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"state\":0", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStreamEndsOnItsOwnSoTheSessionCanBeRenewed()
    {
        using var client = await SignedInClientAsync();

        var started = DateTimeOffset.UtcNow;
        using var response = await client.GetAsync("/api/events", CancellationToken.None);
        await response.Content.ReadAsStringAsync(CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - started;

        // Reading to the end returned at all, which is the assertion: an unbounded stream would
        // have hung here until the test run was killed.
        Assert.True(elapsed < TimeSpan.FromSeconds(30), $"The stream took {elapsed} to end.");
    }

    [Fact]
    public async Task AChangeReachesAnOpenStreamWithoutBeingAskedFor()
    {
        using var client = await SignedInClientAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var reader = new StreamReader(stream);

        // Drain the snapshot first, so what is asserted afterwards can only be the push.
        await ReadUntilAsync(reader, "\"blocked\":false");

        var change = await client.PutAsJsonAsync("/api/devices/usb", new { blocked = true }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        var pushed = await ReadUntilAsync(reader, "\"blocked\":true");

        Assert.Contains("event: usb", pushed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads lines until one contains <paramref name="marker"/>, returning everything read. The
    /// stream lifetime is the timeout: if the marker never arrives the reader reaches the end of
    /// the stream and the assertion fails with what did arrive.
    /// </summary>
    private static async Task<string> ReadUntilAsync(StreamReader reader, string marker)
    {
        var seen = new List<string>();

        while (await reader.ReadLineAsync() is { } line)
        {
            seen.Add(line);
            if (line.Contains(marker, StringComparison.Ordinal))
            {
                return string.Join('\n', seen);
            }
        }

        Assert.Fail($"The stream ended without ever carrying '{marker}'. It carried:\n{string.Join('\n', seen)}");
        return string.Empty;
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

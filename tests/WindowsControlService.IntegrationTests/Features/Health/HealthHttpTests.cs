using System.Net.Http.Json;
using System.Text.Json;

namespace WindowsControlService.IntegrationTests.Features.Health;

/// <summary>
/// <c>GET /api/health</c> is the one call the sign-in screen can make before there is a session,
/// which makes both halves of it worth a test: that it carries what that screen needs, and that
/// it carries nothing else.
/// </summary>
public sealed class HealthHttpTests
{
    [Fact]
    public async Task HealthSaysWhichMachineAndWhenTheServiceStarted()
    {
        using var factory = new ServiceApplicationFactory();
        using var client = factory.CreateClient();

        var health = await client.GetFromJsonAsync<JsonElement>("/api/health", CancellationToken.None);

        Assert.Equal(Environment.MachineName, health.GetProperty("machineName").GetString());

        // The instant the host started, taken from the injected clock -- which the factory holds
        // still. An uptime that came from DateTime.UtcNow would be the age of this test run.
        Assert.Equal(
            factory.Clock.GetUtcNow().UtcDateTime,
            health.GetProperty("startedAt").GetDateTime());
    }

    /// <summary>
    /// The stamp is taken when the host starts, not when this endpoint is first called. A
    /// singleton is built on first use, so a start time recorded in a constructor would be the
    /// instant of the first request -- an uptime that reads as zero however long the service has
    /// really been up, and only on the machine where nobody had opened the page yet.
    /// </summary>
    [Fact]
    public async Task TheStartIsStampedAtStartupAndNotAtTheFirstCall()
    {
        using var factory = new ServiceApplicationFactory();
        using var client = factory.CreateClient();

        var startup = factory.Clock.GetUtcNow().UtcDateTime;
        factory.Clock.Advance(TimeSpan.FromHours(30));

        var health = await client.GetFromJsonAsync<JsonElement>("/api/health", CancellationToken.None);

        Assert.Equal(startup, health.GetProperty("startedAt").GetDateTime());
        // And the answer's own clock has moved, so the browser subtracting one from the other
        // gets the 30 hours rather than nothing.
        Assert.Equal(
            startup.AddHours(30),
            health.GetProperty("timestamp").GetDateTime());
    }

    /// <summary>
    /// Anonymous means everything here is readable by anyone who can reach the port. What the
    /// machine is configured to block is not in this answer, and this is what fails if someone
    /// adds it: the sign-in screen would then be publishing the policy it exists to protect.
    /// </summary>
    [Fact]
    public async Task HealthCarriesNothingAboutWhatTheMachineBlocks()
    {
        using var factory = new ServiceApplicationFactory();
        using var client = factory.CreateClient();

        var health = await client.GetFromJsonAsync<JsonElement>("/api/health", CancellationToken.None);

        var properties = health.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(
            ["status", "version", "machineName", "startedAt", "timestamp"],
            properties);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace WindowsControlService.IntegrationTests.Features.Authentication;

public sealed class SessionPersistenceTests : IDisposable
{
    private readonly TemporaryDirectory _data = new("wcs-restart-tests");

    public void Dispose() => _data.Dispose();

    [Fact]
    public async Task ASessionSurvivesTheServiceRestarting()
    {
        Directory.CreateDirectory(_data.Path);
        string cookie;

        using (var first = new ServiceApplicationFactory(_data.Path).WithGenerousLoginLimit())
        {
            using var client = first.CreateClient();
            await client.PostAsJsonAsync("/api/auth/password", new { password = ServiceApplicationFactory.TestPassword }, CancellationToken.None);

            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ServiceApplicationFactory.TestPassword }, CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            cookie = Assert.Single(
                login.Headers.GetValues("Set-Cookie"),
                value => value.Contains("wcs_session", StringComparison.Ordinal));
        }

        // Sessions used to live in an in-memory dictionary and died with the process. They now
        // live in the cookie, validated against the stored security stamp, so a restart does not
        // sign anyone out.
        using var second = new ServiceApplicationFactory(_data.Path).WithGenerousLoginLimit();
        using var restarted = second.CreateClient();
        restarted.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        var session = await restarted.GetFromJsonAsync<JsonElement>("/api/auth/session", CancellationToken.None);

        Assert.True(session.GetProperty("initialized").GetBoolean());
        Assert.True(session.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task TheSecondStartDoesNotReapplyMigrations()
    {
        Directory.CreateDirectory(_data.Path);

        using (var first = new ServiceApplicationFactory(_data.Path))
        {
            using var client = first.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", CancellationToken.None)).StatusCode);
        }

        using var second = new ServiceApplicationFactory(_data.Path);
        using var again = second.CreateClient();

        // A second run over the same database must simply start. DbUp skipping already applied
        // scripts is what makes that true.
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/health", CancellationToken.None)).StatusCode);
    }
}

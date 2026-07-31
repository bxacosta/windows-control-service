using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace WindowsControlService.IntegrationTests.Features.Authentication;

public sealed class SessionPersistenceTests : IDisposable
{
    private const string Password = "una-contrasena-larga";

    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "wcs-restart-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    [Fact]
    public async Task ASessionSurvivesTheServiceRestarting()
    {
        Directory.CreateDirectory(_dataDirectory);
        string cookie;

        using (var first = new ServiceApplicationFactory(_dataDirectory).WithGenerousLoginLimit())
        {
            using var client = first.CreateClient();
            await client.PostAsJsonAsync("/api/auth/password", new { password = Password }, CancellationToken.None);

            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password }, CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            cookie = Assert.Single(
                login.Headers.GetValues("Set-Cookie"),
                value => value.Contains("wcs_session", StringComparison.Ordinal));
        }

        // Sessions used to live in an in-memory dictionary and died with the process. They now
        // live in the cookie, validated against the stored security stamp, so a restart does not
        // sign anyone out.
        using var second = new ServiceApplicationFactory(_dataDirectory).WithGenerousLoginLimit();
        using var restarted = second.CreateClient();
        restarted.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        var session = await restarted.GetFromJsonAsync<JsonElement>("/api/auth/session", CancellationToken.None);

        Assert.True(session.GetProperty("initialized").GetBoolean());
        Assert.True(session.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task TheSecondStartDoesNotReapplyMigrations()
    {
        Directory.CreateDirectory(_dataDirectory);

        using (var first = new ServiceApplicationFactory(_dataDirectory))
        {
            using var client = first.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", CancellationToken.None)).StatusCode);
        }

        using var second = new ServiceApplicationFactory(_dataDirectory);
        using var again = second.CreateClient();

        // A second run over the same database must simply start. DbUp skipping already applied
        // scripts is what makes that true.
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/health", CancellationToken.None)).StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace WindowsControlService.IntegrationTests.Features.Authentication;

/// <summary>
/// The HTTP contract, driven end to end against the real application. Every case in the phase 3
/// table lives here.
/// </summary>
public sealed class AuthenticationHttpTests : IDisposable
{
    private const string Password = "una-contrasena-larga";

    private readonly ServiceApplicationFactory _factory = new ServiceApplicationFactory().WithGenerousLoginLimit();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task HealthIsPublic()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal("running", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));

        // Z, not +00:00: the API contract fixes the shape of every timestamp it emits.
        Assert.EndsWith("Z", body.GetProperty("timestamp").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProtectedEndpointWithoutACookieAnswersUnauthorizedNotARedirect()
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsync("/api/auth/logout", content: null, CancellationToken.None);

        // The whole reason the framework's cookie handler is usable here: .NET 10 answers 401 for
        // API endpoints instead of 302 towards a login page.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task AFreshInstallReportsItselfUninitialised()
    {
        using var client = _factory.CreateClient();

        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session", CancellationToken.None);

        Assert.False(session.GetProperty("initialized").GetBoolean());
        Assert.False(session.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task AShortPasswordIsRejected()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/password", new { password = "abc" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ConfiguringAPasswordFlipsTheSessionToInitialised()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/password", new { password = Password }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session", CancellationToken.None);
        Assert.True(session.GetProperty("initialized").GetBoolean());
    }

    [Fact]
    public async Task ConfiguringAPasswordTwiceIsAConflict()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/password", new { password = Password }, CancellationToken.None);

        var second = await client.PostAsJsonAsync("/api/auth/password", new { password = "otra-contrasena" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task TheWrongPasswordDoesNotSignAnyoneIn()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/password", new { password = Password }, CancellationToken.None);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { password = "no-es-la-buena" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(SetCookieValues(response), value => value.Contains("wcs_session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheRightPasswordIssuesTheSessionCookie()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/password", new { password = Password }, CancellationToken.None);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { password = Password }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(SetCookieValues(response), value => value.Contains("wcs_session", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AValidCookieOpensTheProtectedEndpoints()
    {
        using var client = await SignedInClientAsync();

        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session", CancellationToken.None);
        Assert.True(session.GetProperty("authenticated").GetBoolean());

        var logout = await client.PostAsync("/api/auth/logout", content: null, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
    }

    [Fact]
    public async Task ChangingThePasswordWithoutTheCurrentOneIsRejected()
    {
        using var client = await SignedInClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/auth/password",
            new { currentPassword = "no-es-la-buena", newPassword = "otra-contrasena-larga" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangingThePasswordInvalidatesTheSessionThatChangedIt()
    {
        using var client = await SignedInClientAsync();

        var change = await client.PutAsJsonAsync(
            "/api/auth/password",
            new { currentPassword = Password, newPassword = "otra-contrasena-larga" },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // The security stamp rotated, so every open session dies -- including this one. This is
        // one of the two tests that justify the whole phase.
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session", CancellationToken.None);
        Assert.False(session.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task ASessionExpiresOnceTheClockPassesTheTimeout()
    {
        using var client = await SignedInClientAsync();

        _factory.Clock.Advance(TimeSpan.FromHours(2));

        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session", CancellationToken.None);
        Assert.False(session.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task AMissingFieldIsAValidationProblem()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/password", new { }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task TheOpenApiDocumentDescribesTheAuthEndpoints()
    {
        using var client = _factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", CancellationToken.None);

        Assert.Equal("3.1.1", document.GetProperty("openapi").GetString());
        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/auth/login", out _));
        Assert.True(paths.TryGetProperty("/api/health", out _));
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/password", new { password = Password }, CancellationToken.None);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return client;
    }

    private static IReadOnlyList<string> SetCookieValues(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];
}

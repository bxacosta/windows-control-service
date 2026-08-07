using System.Net;

namespace WindowsControlService.IntegrationTests.WebInterface;

/// <summary>
/// Serving an interface must not change how the API answers. These pin the boundary: the shell
/// is public and cached by revalidation, everything behind it still demands a session, and a
/// path that matches nothing is a 404 rather than the shell's HTML.
/// </summary>
public sealed class InterfaceHostingTests : IDisposable
{
    private readonly ServiceApplicationFactory _factory = new();

    [Fact]
    public async Task TheRootServesTheShell()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id=\"notices\"", body, StringComparison.Ordinal);
        Assert.Contains("/js/app.js", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticFilesRevalidateInsteadOfBeingVersioned()
    {
        using var client = _factory.CreateClient();

        using var first = await client.GetAsync(new Uri("/css/app.css", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("no-cache", first.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.NotNull(first.Headers.ETag);

        // This is what replaces a version marker in the URL: the browser asks, and gets 304
        // until the file on disk actually changes.
        using var conditional = new HttpRequestMessage(HttpMethod.Get, new Uri("/css/app.css", UriKind.Relative));
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task TheInterfaceDoesNotMakeTheApiGuessWhoIsAsking()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/applications", UriKind.Relative));

        // 401 and not 302: a browser asking for the shell is one thing, an unauthenticated API
        // call is another, and static files must not blur them.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AnUnknownPathIsNotAnsweredWithTheShell()
    {
        using var client = _factory.CreateClient();

        // No MapFallbackToFile on purpose: with one, a mistyped /api/... path would answer 200
        // and HTML, which no client can tell from a working call that returned nonsense.
        using var page = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative));
        using var api = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
    }

    [Fact]
    public async Task TheShellIsServedBeforeAuthentication()
    {
        using var client = _factory.CreateClient();

        // Reaching the login screen cannot require being logged in. The shell carries no data.
        using var response = await client.GetAsync(new Uri("/index.html", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}

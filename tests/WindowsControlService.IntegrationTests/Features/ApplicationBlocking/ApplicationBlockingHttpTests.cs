using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Features.ApplicationBlocking;

public sealed class ApplicationBlockingHttpTests : IDisposable
{
    private const string Password = "una-contrasena-larga";

    private readonly ServiceApplicationFactory _factory = new ServiceApplicationFactory()
        .WithGenerousLoginLimit()
        // Long enough that the reconciliation worker never fires mid-test and changes the fake's
        // state underneath an assertion.
        .With("ApplicationBlocking:ReconciliationInterval", "01:00:00");

    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "wcs-blocking-http", Guid.NewGuid().ToString("N"));

    public ApplicationBlockingHttpTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        _factory.Dispose();

        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    [Fact]
    public async Task TheListRequiresASession()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/applications", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddingReturnsCreatedWithALocation()
    {
        using var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/applications",
            new { executablePath = CreateExecutable("target.exe"), name = "Target" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal($"/api/applications/{id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task TheListSaysWhichAttributeTheRuleMatchesOn()
    {
        using var client = await SignedInClientAsync();
        var path = CreateExecutable("renamed.exe");
        _factory.ExecutableReader.WithOriginalFileName(path, "the-real-name.exe");

        await client.PostAsJsonAsync("/api/applications", new { executablePath = path, name = "Target" }, CancellationToken.None);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/applications", CancellationToken.None);
        var entry = list.EnumerateArray().Single();

        // Both halves are exposed: the value explains why renaming the executable changes
        // nothing, and the attribute keeps the interface from claiming every rule matches on
        // OriginalFilename when plenty of binaries do not carry one.
        Assert.Equal("FileName", entry.GetProperty("matchAttribute").GetString());
        Assert.Equal("the-real-name.exe", entry.GetProperty("matchValue").GetString());
        Assert.True(entry.GetProperty("isEnabled").GetBoolean());
        Assert.EndsWith("Z", entry.GetProperty("createdAt").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APathThatDoesNotExistIsABadRequest()
    {
        using var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/applications",
            new { executablePath = Path.Combine(_workDirectory, "missing.exe"), name = "Nope" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ADuplicateIsAConflict()
    {
        using var client = await SignedInClientAsync();
        var path = CreateExecutable("target.exe");
        await client.PostAsJsonAsync("/api/applications", new { executablePath = path, name = "Target" }, CancellationToken.None);

        var again = await client.PostAsJsonAsync(
            "/api/applications",
            new { executablePath = path, name = "Target again" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task AFailingPolicyIsAnInternalErrorAndNothingIsRecorded()
    {
        using var client = await SignedInClientAsync();
        _factory.CodeIntegrity.ApplyFailure = new Error(ErrorCode.OperationFailed, "Windows refused it.");

        var response = await client.PostAsJsonAsync(
            "/api/applications",
            new { executablePath = CreateExecutable("target.exe"), name = "Target" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/applications", CancellationToken.None);
        Assert.Empty(list.EnumerateArray());
    }

    [Fact]
    public async Task MissingCodeIntegrityToolingIsServiceUnavailable()
    {
        using var client = await SignedInClientAsync();
        _factory.CodeIntegrity.ApplyFailure = new Error(ErrorCode.PlatformUnavailable, "CiTool is not here.");

        var response = await client.PostAsJsonAsync(
            "/api/applications",
            new { executablePath = CreateExecutable("target.exe"), name = "Target" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GettingAnUnknownIdIsNotFound()
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync("/api/applications/404", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DisablingKeepsTheEntry()
    {
        using var client = await SignedInClientAsync();
        var id = await AddAsync(client, "target.exe");

        var patch = await client.PatchAsJsonAsync($"/api/applications/{id}", new { enabled = false }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var entry = await client.GetFromJsonAsync<JsonElement>($"/api/applications/{id}", CancellationToken.None);
        Assert.False(entry.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task PatchWithoutTheFieldIsABadRequest()
    {
        using var client = await SignedInClientAsync();
        var id = await AddAsync(client, "target.exe");

        var patch = await client.PatchAsJsonAsync($"/api/applications/{id}", new { }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    [Fact]
    public async Task DeletingReturnsNoContent()
    {
        using var client = await SignedInClientAsync();
        var id = await AddAsync(client, "target.exe");

        var response = await client.DeleteAsync($"/api/applications/{id}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/applications/{id}", CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task AFailedDeletionLeavesTheEntryBlocked()
    {
        using var client = await SignedInClientAsync();
        var id = await AddAsync(client, "target.exe");
        await AddAsync(client, "other.exe");
        _factory.CodeIntegrity.ApplyFailure = new Error(ErrorCode.OperationFailed, "no");

        var response = await client.DeleteAsync($"/api/applications/{id}", CancellationToken.None);

        // The contract is explicit: on a 500 the entry still exists and stays blocked.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/applications/{id}", CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task ThePolicyStateEndpointReportsTheThirdState()
    {
        using var client = await SignedInClientAsync();
        _factory.CodeIntegrity.State = PolicyState.Unknown;

        var state = await client.GetFromJsonAsync<JsonElement>("/api/applications/policy-state", CancellationToken.None);

        // Unknown has to be distinguishable from "there is no policy", or the interface tells the
        // user something false.
        Assert.Equal("Unknown", state.GetProperty("state").GetString());
        Assert.Equal(0, state.GetProperty("enabledRuleCount").GetInt32());

        // Spelled out because the property is the enum now, not its name: as a number the
        // browser would be comparing against member order, and Unknown and NotEnforced are the
        // two that mean opposite things. The event stream carries the same record and has the
        // same assertion, in EventStreamTests.
        Assert.Equal(JsonValueKind.String, state.GetProperty("state").ValueKind);
    }

    [Fact]
    public async Task ThePolicyStateRouteIsNotParsedAsAnIdentifier()
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync("/api/applications/policy-state", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<long> AddAsync(HttpClient client, string fileName)
    {
        var response = await client.PostAsJsonAsync(
            "/api/applications",
            new { executablePath = CreateExecutable(fileName), name = fileName },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        return body.GetProperty("id").GetInt64();
    }

    /// <summary>
    /// A file on disk carrying an OriginalFilename, because a binary without one is refused now:
    /// a rule built from the name on disk is one WDAC never matches.
    /// </summary>
    private string CreateExecutable(string fileName)
    {
        var path = Path.Combine(_workDirectory, fileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "not a real executable, but it exists on disk");
        }

        _factory.ExecutableReader.WithOriginalFileName(path, fileName);

        return path;
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

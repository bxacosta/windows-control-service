using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.IntegrationTests.Features.DeviceControl;

/// <summary>
/// Drives the HTTP contract with <c>FakeUsbStorageSwitch</c>. Nothing here touches the real
/// registry; that is the separate, capture-and-restore test.
/// </summary>
public sealed class DeviceControlHttpTests : IDisposable
{
    private const string Password = "una-contrasena-larga";

    private readonly ServiceApplicationFactory _factory = new ServiceApplicationFactory()
        .WithGenerousLoginLimit()
        .With("ApplicationBlocking:ReconciliationInterval", "01:00:00");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ReadingTheStateRequiresASession()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/devices/usb", CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task AFreshMachineReportsUnblockedWithNoTimestamp()
    {
        using var client = await SignedInClientAsync();

        var status = await client.GetFromJsonAsync<JsonElement>("/api/devices/usb", CancellationToken.None);

        Assert.False(status.GetProperty("blocked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastModified").ValueKind);
    }

    [Fact]
    public async Task BlockingFlipsTheSwitchAndStampsTheTime()
    {
        using var client = await SignedInClientAsync();

        var response = await client.PutAsJsonAsync("/api/devices/usb", new { blocked = true }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_factory.UsbStorage.Blocked);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/devices/usb", CancellationToken.None);
        Assert.True(status.GetProperty("blocked").GetBoolean());
        Assert.EndsWith("Z", status.GetProperty("lastModified").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnblockingFlipsItBack()
    {
        using var client = await SignedInClientAsync();
        await client.PutAsJsonAsync("/api/devices/usb", new { blocked = true }, CancellationToken.None);

        await client.PutAsJsonAsync("/api/devices/usb", new { blocked = false }, CancellationToken.None);

        Assert.False(_factory.UsbStorage.Blocked);
    }

    [Fact]
    public async Task AChangeMadeOutsideTheServiceIsReflected()
    {
        using var client = await SignedInClientAsync();

        // The registry is the source of truth, not a column in the database.
        _factory.UsbStorage.Blocked = true;

        var status = await client.GetFromJsonAsync<JsonElement>("/api/devices/usb", CancellationToken.None);
        Assert.True(status.GetProperty("blocked").GetBoolean());
    }

    [Fact]
    public async Task AnEmptyBodyIsRejectedRatherThanReadAsFalse()
    {
        using var client = await SignedInClientAsync();
        await client.PutAsJsonAsync("/api/devices/usb", new { blocked = true }, CancellationToken.None);

        var response = await client.PutAsJsonAsync("/api/devices/usb", new { }, CancellationToken.None);

        // With a non-nullable bool this would have unblocked the machine.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(_factory.UsbStorage.Blocked);
    }

    [Fact]
    public async Task MissingPrivilegesAnswerForbiddenNotAGenericError()
    {
        using var client = await SignedInClientAsync();
        _factory.UsbStorage.Failure = new Error(ErrorCode.AccessDenied, "Administrator rights are required.");

        var response = await client.PutAsJsonAsync("/api/devices/usb", new { blocked = true }, CancellationToken.None);

        // "Run as administrator" is only useful advice when that is really the problem.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AMissingRegistryKeyAnswersServiceUnavailable()
    {
        using var client = await SignedInClientAsync();
        _factory.UsbStorage.Failure = new Error(ErrorCode.PlatformUnavailable, "The key is not present.");

        var response = await client.GetAsync("/api/devices/usb", CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task BlockingSomethingAlreadyBlockedDoesNotMoveTheTimestamp()
    {
        using var client = await SignedInClientAsync();
        await client.PutAsJsonAsync("/api/devices/usb", new { blocked = true }, CancellationToken.None);
        var first = (await client.GetFromJsonAsync<JsonElement>("/api/devices/usb", CancellationToken.None))
            .GetProperty("lastModified").GetString();

        // Under the 30 minute session timeout: a longer jump would expire the cookie and the
        // test would fail for a reason that has nothing to do with timestamps.
        _factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var repeat = await client.PutAsJsonAsync("/api/devices/usb", new { blocked = true }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

        var second = (await client.GetFromJsonAsync<JsonElement>("/api/devices/usb", CancellationToken.None))
            .GetProperty("lastModified").GetString();

        // Nothing changed, so nothing should claim it did.
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task AFailedRegistryWriteLeavesNoTimestampBehind()
    {
        using var client = await SignedInClientAsync();
        _factory.UsbStorage.Failure = new Error(ErrorCode.AccessDenied, "no");

        await client.PutAsJsonAsync("/api/devices/usb", new { blocked = true }, CancellationToken.None);

        _factory.UsbStorage.Failure = null;
        var status = await client.GetFromJsonAsync<JsonElement>("/api/devices/usb", CancellationToken.None);

        // Recording a change that never happened is worse than recording nothing.
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastModified").ValueKind);
        Assert.False(status.GetProperty("blocked").GetBoolean());
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

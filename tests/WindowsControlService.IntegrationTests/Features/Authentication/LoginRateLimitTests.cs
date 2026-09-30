using System.Net;
using System.Net.Http.Json;

namespace WindowsControlService.IntegrationTests.Features.Authentication;

/// <summary>
/// The only tests that keep the default limit. Each test gets its own host, and with it its own
/// limiter, so exhausting the window here cannot leak into another test.
/// </summary>
public sealed class LoginRateLimitTests : IDisposable
{
    private readonly ServiceApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task TheSixthAttemptInAMinuteIsRejectedWithTooManyRequests()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/password", new { password = ServiceApplicationFactory.TestPassword }, CancellationToken.None);

        var codes = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { password = "not-the-right-one-2026" },
                CancellationToken.None);

            codes.Add(response.StatusCode);
        }

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.Unauthorized, 5), codes.Take(5));

        // 429, not 503. The rate limiter's default rejection code is 503 Service Unavailable,
        // which a client reads as "the service is down" rather than "you are going too fast".
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[5]);
    }
}

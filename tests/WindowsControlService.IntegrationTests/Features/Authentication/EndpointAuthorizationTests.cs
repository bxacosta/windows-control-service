using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace WindowsControlService.IntegrationTests.Features.Authentication;

/// <summary>
/// Authorization is declared endpoint by endpoint, so forgetting it on a new one is one missing
/// call. This reads every route the service maps and fails on any API endpoint that answers
/// without a session and is not on the list below.
/// </summary>
public sealed class EndpointAuthorizationTests
{
    /// <summary>What the sign-in screen needs before there is a session, and nothing else.</summary>
    private static readonly string[] Anonymous =
    [
        "GET /api/auth/session",
        "GET /api/health",
        "POST /api/auth/login",
        "POST /api/auth/password",
    ];

    [Fact]
    public void EveryApiEndpointRequiresASessionExceptTheOnesTheSignInScreenNeeds()
    {
        using var factory = new ServiceApplicationFactory();

        var open = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => $"{method} {endpoint.RoutePattern.RawText!.TrimEnd('/')}"))
            .Order(StringComparer.Ordinal);

        Assert.Equal(Anonymous, open);
    }
}

using System.Reflection;

namespace WindowsControlService.Features.Health;

/// <param name="Timestamp">
/// UTC, like every timestamp this API produces. A UTC <see cref="DateTime"/> rather than a
/// <see cref="DateTimeOffset"/> so it serialises as "...Z" instead of "...+00:00", which is what
/// the API contract specifies.
/// </param>
public sealed record HealthResponse(string Status, string Version, DateTime Timestamp);

public static class HealthEndpoints
{
    private static readonly string Version =
        typeof(HealthEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HealthEndpoints).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/health", (TimeProvider clock) =>
                TypedResults.Ok(new HealthResponse("running", Version, clock.GetUtcNow().UtcDateTime)))
            .AllowAnonymous()
            .WithName("GetHealth");

        return endpoints;
    }
}

using System.Reflection;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.Health;

/// <param name="StartedAt">
/// When the service started, not how long it has been up. A duration computed here would be
/// stale by the time it was painted, and the interface would have no way to keep it current
/// without asking again every minute. The instant is a fact that does not decay.
/// </param>
/// <param name="Timestamp">
/// UTC, like every timestamp this API produces. A UTC <see cref="DateTime"/> rather than a
/// <see cref="DateTimeOffset"/> so it serialises as "...Z" instead of "...+00:00", which is what
/// the API contract specifies.
/// </param>
public sealed record HealthResponse(
    string Status,
    string Version,
    string MachineName,
    DateTime StartedAt,
    DateTime Timestamp);

public static class HealthEndpoints
{
    private static readonly string Version =
        typeof(HealthEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HealthEndpoints).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static IServiceCollection AddHealth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Registered twice on purpose, and both are needed: the hosted service is what stamps
        // the start, and the singleton is what the endpoint reads. Resolving the same instance
        // for the hosted registration is what keeps them from being two objects.
        services.AddSingleton<ServiceStartTime>();
        services.AddHostedService(provider => provider.GetRequiredService<ServiceStartTime>());

        return services;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Anonymous, and that is what decides what may be in the answer. The sign-in screen
        // reads this call before there is a session, so everything here is visible to anyone who
        // can reach the port: the machine's own name, how long its service has been up, and the
        // version. What the machine is *configured* to block is not in that list and must not
        // be added to it.
        endpoints.MapGet("/api/health", (
                ServiceStartTime start,
                IMachineIdentity machine,
                TimeProvider clock) =>
                TypedResults.Ok(new HealthResponse(
                    "running",
                    Version,
                    machine.MachineName,
                    start.StartedAt,
                    clock.GetUtcNow().UtcDateTime)))
            .AllowAnonymous()
            .WithName("GetHealth");

        return endpoints;
    }
}

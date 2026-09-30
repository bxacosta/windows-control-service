using System.Reflection;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.Health;

public static class HealthEndpoints
{
    private static readonly string Version =
        typeof(HealthEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HealthEndpoints).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder endpoints)
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

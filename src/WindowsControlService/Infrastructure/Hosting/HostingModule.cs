using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WindowsControlService.Infrastructure.Hosting;

public static class HostingModule
{
    /// <summary>
    /// The cross-cutting singletons every feature builds on: the clock and the one lock that
    /// serialises machine-state changes.
    /// </summary>
    public static IServiceCollection AddServiceInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Registered here on purpose. Neither WebApplication.CreateBuilder nor
        // Host.CreateApplicationBuilder puts TimeProvider in the container on .NET 10 --
        // checked against SDK 10.0.111, both return null. Without this line every injection of
        // TimeProvider fails at startup and the temptation is to reach for DateTime.UtcNow.
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<ISequentialExecutor, SequentialExecutor>();

        return services;
    }
}

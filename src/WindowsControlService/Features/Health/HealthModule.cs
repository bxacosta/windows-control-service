namespace WindowsControlService.Features.Health;

public static class HealthModule
{
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
}

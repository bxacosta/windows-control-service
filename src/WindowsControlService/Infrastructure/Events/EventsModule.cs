using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WindowsControlService.Infrastructure.Events;

public static class EventsModule
{
    public static IServiceCollection AddServiceEvents(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ServiceEventOptions>()
            .Bind(configuration.GetSection(ServiceEventOptions.Section))
            .ValidateDataAnnotations()
            .Validate(
                // A floor rather than a policy: anything under a second is a reconnect storm,
                // and the tests legitimately want a stream that ends in a couple of seconds.
                options => options.StreamLifetime >= TimeSpan.FromSeconds(1),
                $"{ServiceEventOptions.Section}:{nameof(ServiceEventOptions.StreamLifetime)} must be at least one second.")
            // The other end of this range is a cross-module constraint and lives in Program.cs:
            // it depends on the authentication options, and Infrastructure may not reach into a
            // feature to read them.
            .ValidateOnStart();

        services.TryAddSingleton<IServiceEventBroadcaster, ServiceEventBroadcaster>();

        return services;
    }
}

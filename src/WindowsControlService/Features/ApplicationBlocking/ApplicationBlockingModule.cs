namespace WindowsControlService.Features.ApplicationBlocking;

public static class ApplicationBlockingModule
{
    public static IServiceCollection AddApplicationBlocking(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ApplicationBlockingOptions>()
            .Bind(configuration.GetSection(ApplicationBlockingOptions.Section))
            .Validate(
                options => options.ReconciliationInterval >= TimeSpan.FromSeconds(10),
                $"{ApplicationBlockingOptions.Section}:{nameof(ApplicationBlockingOptions.ReconciliationInterval)} must be at least ten seconds.")
            // A zero interval would spin the worker without pause. Refusing to start says so.
            .ValidateOnStart();

        services.AddSingleton<IBlockedApplicationRepository, BlockedApplicationRepository>();
        services.AddSingleton<IApplicationBlockingService, ApplicationBlockingService>();
        services.AddHostedService<PolicyReconciliationWorker>();

        return services;
    }
}

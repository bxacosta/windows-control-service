using WindowsControlService.Infrastructure.Events;

namespace WindowsControlService.Features.AccessHistory;

public static class AccessHistoryModule
{
    public static IServiceCollection AddAccessHistory(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AccessHistoryOptions>()
            .Bind(configuration.GetSection(AccessHistoryOptions.Section))
            .ValidateDataAnnotations()
            .Validate(
                options => options.IngestionInterval >= TimeSpan.FromSeconds(10),
                $"{AccessHistoryOptions.Section}:{nameof(AccessHistoryOptions.IngestionInterval)} must be at least ten seconds.")
            .Validate(
                options => options.IngestionWindow > TimeSpan.Zero,
                $"{AccessHistoryOptions.Section}:{nameof(AccessHistoryOptions.IngestionWindow)} must be greater than zero.")
            .Validate(
                options => options.MaxPlausibleSessionLength > TimeSpan.Zero,
                $"{AccessHistoryOptions.Section}:{nameof(AccessHistoryOptions.MaxPlausibleSessionLength)} must be greater than zero.")
            .Validate(
                options => options.DefaultPageSize <= options.MaxPageSize,
                $"{AccessHistoryOptions.Section}:{nameof(AccessHistoryOptions.DefaultPageSize)} cannot exceed MaxPageSize.")
            .ValidateOnStart();

        services.AddSingleton<ILogonEventRepository, LogonEventRepository>();
        services.AddSingleton<IAccessHistoryService, AccessHistoryService>();
        services.AddSingleton<IServiceEventSnapshot, AccessHistorySnapshot>();

        // No ISequentialExecutor here: this worker touches no machine state, only its own table,
        // and INSERT OR IGNORE inside a transaction is already safe on its own.
        services.AddHostedService<AccessHistoryIngestionWorker>();

        return services;
    }
}

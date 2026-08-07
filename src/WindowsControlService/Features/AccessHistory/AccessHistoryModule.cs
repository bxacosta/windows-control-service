using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Events;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.AccessHistory;

/// <summary>
/// Reads the Windows log on a timer and writes to our own table.
/// </summary>
/// <remarks>
/// It runs on a timer rather than during UI requests on purpose. The history exists so there is
/// a record whether or not anyone is looking; tying ingestion to someone opening the interface
/// would leave permanent holes as soon as Windows rotated the log during a quiet period.
/// </remarks>
public sealed class AccessHistoryIngestionWorker(
    IAccessHistoryService history,
    IServiceEventBroadcaster events,
    IOptions<AccessHistoryOptions> options,
    ILogger<AccessHistoryIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.IngestionInterval);

        try
        {
            // do/while, so the first ingestion happens at startup instead of an interval later.
            do
            {
                try
                {
                    var inserted = await history.IngestAsync(stoppingToken);
                    if (inserted > 0 && logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("Ingested {Count} new logon event(s).", inserted);
                    }

                    // Nothing new means nothing to say, and with no listener the count query
                    // would be work done for an empty room.
                    if (inserted > 0 && events.HasSubscribers)
                    {
                        var page = await history.GetTimelineAsync(limit: 1, offset: 0, origin: null, stoppingToken);
                        events.Publish(new ServiceEvent(AccessHistorySnapshot.EventName, new AccessHistoryTotal(page.Total)));
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
#pragma warning disable CA1031 // A failing cycle must never take the worker down with it.
                catch (Exception exception)
                {
                    logger.LogError(exception, "Access history ingestion cycle failed.");
                }
#pragma warning restore CA1031
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}

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

    public static IEndpointRouteBuilder MapAccessHistory(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/access-history", GetTimelineAsync)
            .RequireAuthorization()
            .WithName("GetAccessHistory");

        return endpoints;
    }

    private static async Task<Results<Ok<AccessHistoryPage>, ProblemHttpResult>> GetTimelineAsync(
        HttpContext context,
        IAccessHistoryService history,
        int? limit = null,
        int? offset = null,
        string? origin = null)
    {
        if (!TryParseOrigin(origin, out var parsed))
        {
            return new Error(ErrorCode.Invalid, "origin must be local, remote or all.").ToHttpResult();
        }

        return TypedResults.Ok(await history.GetTimelineAsync(limit, offset, parsed, context.RequestAborted));
    }

    /// <summary>
    /// Accepts local, remote, all and absent, any casing. <c>Unknown</c> is deliberately not a
    /// valid filter: it is an internal state, not something a caller should ask for.
    /// </summary>
    private static bool TryParseOrigin(string? origin, out LogonOrigin? parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(origin) || string.Equals(origin, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(origin, "local", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LogonOrigin.Local;
            return true;
        }

        if (string.Equals(origin, "remote", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LogonOrigin.Remote;
            return true;
        }

        return false;
    }
}

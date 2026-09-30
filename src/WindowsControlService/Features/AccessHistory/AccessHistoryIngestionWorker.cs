using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Events;

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
                    if (inserted > 0 && events.HasSubscribers
                        && await AccessHistorySnapshot.CaptureAsync(history, stoppingToken) is { } snapshot)
                    {
                        events.Publish(snapshot);
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

using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Events;

namespace WindowsControlService.Features.ApplicationBlocking;

public sealed class ApplicationBlockingOptions
{
    public const string Section = "ApplicationBlocking";

    /// <summary>
    /// How often the deployed policy is compared against the database. An administrator can run
    /// <c>CiTool --remove-policy</c> and nothing can prevent that; this is what notices.
    /// </summary>
    public TimeSpan ReconciliationInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class PolicyReconciliationWorker(
    IApplicationBlockingService blocking,
    IServiceEventBroadcaster events,
    IOptions<ApplicationBlockingOptions> options,
    ILogger<PolicyReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ReconciliationInterval);

        try
        {
            // do/while, not while: a plain while leaves a whole interval after every start with
            // the policy unverified, which is exactly when it is most likely to be wrong.
            do
            {
                try
                {
                    var result = await blocking.ReconcileAsync(stoppingToken);
                    if (result.IsFailure)
                    {
                        logger.LogError("Reconciliation reported {Code}: {Message}", result.Error.Code, result.Error.Message);
                    }

                    // Only with a listener. Reading the state costs a CiTool process, and with
                    // no interface open nobody would read the answer.
                    if (events.HasSubscribers)
                    {
                        var snapshot = await PolicyStateSnapshot.CaptureAsync(blocking, stoppingToken);
                        if (snapshot is not null)
                        {
                            events.Publish(snapshot);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
#pragma warning disable CA1031 // A failing cycle must never take the worker down with it.
                catch (Exception exception)
                {
                    logger.LogError(exception, "Reconciliation cycle failed.");
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

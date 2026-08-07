using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Events;

namespace WindowsControlService.Features.Events;

/// <summary>
/// One stream carrying every push the interface needs, so a browser holds one connection rather
/// than one per section. That matters more than it looks: this is plain HTTP, which means
/// HTTP/1.1 and six connections per origin.
/// </summary>
public static class EventStreamEndpoints
{
    public static IEndpointRouteBuilder MapEventStream(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/events", StreamEvents)
            .RequireAuthorization()
            .WithName("StreamEvents");

        // Deliberately no rate limiting. There is no global limiter today, and if one is ever
        // added this endpoint has to stay outside it: the client reconnects by design, and a
        // throttled reconnect is an interface that stops updating without saying so.
        return endpoints;
    }

    private static ServerSentEventsResult<object> StreamEvents(
        HttpContext context,
        IServiceEventBroadcaster stream,
        IEnumerable<IServiceEventSnapshot> snapshots,
        IOptions<ServiceEventOptions> options,
        IHostApplicationLifetime lifetime) =>
        TypedResults.ServerSentEvents(
            ReadAsync(stream, snapshots, options.Value, lifetime.ApplicationStopping, context.RequestAborted));

    private static async IAsyncEnumerable<SseItem<object>> ReadAsync(
        IServiceEventBroadcaster stream,
        IEnumerable<IServiceEventSnapshot> snapshots,
        ServiceEventOptions options,
        CancellationToken applicationStopping,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping, requestAborted);

        // ApplicationStopping is in there for a reason: ShutdownTimeout is seventy seconds, and
        // a stream that ignores it turns every service stop into a seventy second wait.
        ending.CancelAfter(options.StreamLifetime);

        // Subscribe before capturing the snapshots. The other order loses anything that changes
        // between reading the current state and starting to listen.
        using var subscription = stream.Subscribe();

        foreach (var snapshot in snapshots)
        {
            var captured = await snapshot.CaptureAsync(ending.Token);
            if (captured is not null)
            {
                yield return new SseItem<object>(captured.Payload, captured.Name);
            }
        }

        while (true)
        {
            var next = await subscription.ReadAsync(ending.Token);
            if (next is null)
            {
                // Lifetime reached, client gone, or the service is stopping. All three end the
                // stream the same way: cleanly, so the browser reconnects by itself.
                yield break;
            }

            yield return new SseItem<object>(next.Payload, next.Name);
        }
    }
}

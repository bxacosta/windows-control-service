namespace WindowsControlService.Infrastructure.Events;

/// <summary>
/// One named update pushed to whoever is watching. The payload is the same shape the matching
/// endpoint returns, so a client never has to learn two representations of the same thing.
/// </summary>
/// <param name="Name">The SSE event name: <c>policy-state</c>, <c>usb</c>, <c>access-history</c>.</param>
public sealed record ServiceEvent(string Name, object Payload);

/// <summary>
/// Produces the current value of one event. A subscriber gets these the moment it connects, so
/// opening a section does not need a separate GET.
/// </summary>
/// <remarks>
/// This is why the stream itself knows nothing about features: each feature contributes its own
/// snapshot, and adding a fourth event never touches the endpoint that serves them.
/// </remarks>
public interface IServiceEventSnapshot
{
    ValueTask<ServiceEvent?> CaptureAsync(CancellationToken cancellationToken);
}

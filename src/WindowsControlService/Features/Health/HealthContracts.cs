namespace WindowsControlService.Features.Health;

/// <param name="StartedAt">
/// When the service started, not how long it has been up. A duration computed here would be
/// stale by the time it was painted, and the interface would have no way to keep it current
/// without asking again every minute. The instant is a fact that does not decay.
/// </param>
/// <param name="Timestamp">
/// UTC, like every timestamp this API produces. A UTC <see cref="DateTime"/> rather than a
/// <see cref="DateTimeOffset"/> so it serialises as "...Z" instead of "...+00:00", which is what
/// the API contract specifies.
/// </param>
public sealed record HealthResponse(
    string Status,
    string Version,
    string MachineName,
    DateTime StartedAt,
    DateTime Timestamp);

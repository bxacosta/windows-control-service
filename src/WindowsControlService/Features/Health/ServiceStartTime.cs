namespace WindowsControlService.Features.Health;

/// <summary>
/// The instant this process began serving, stamped once.
/// </summary>
/// <remarks>
/// A hosted service rather than a constructor, and this is the whole point of the type: a
/// singleton is built the first time something asks for it, so a stamp taken in its constructor
/// would record the first request to <c>/api/health</c> and not the start of the service. The
/// host runs <see cref="StartAsync"/> before the server accepts anything, so by the time any
/// caller can read <see cref="StartedAt"/> it is already the right instant.
///
/// The clock is injected, like everywhere else here: a service that reads
/// <see cref="DateTime"/>.UtcNow cannot be tested against an uptime that is not the age of the
/// test run.
/// </remarks>
public sealed class ServiceStartTime(TimeProvider clock) : IHostedService
{
    /// <summary>UTC, like every timestamp this API produces.</summary>
    public DateTime StartedAt { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartedAt = clock.GetUtcNow().UtcDateTime;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

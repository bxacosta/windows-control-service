namespace WindowsControlService.Features.Health;

/// <summary>
/// The instant this process began serving, stamped once.
/// </summary>
/// <remarks>
/// <para>
/// Stamped in the constructor, and registered as an <see cref="IHostedService"/> only to decide
/// <em>when</em> that constructor runs. A singleton is otherwise built the first time something
/// asks for it, which would record the first request to <c>/api/health</c> rather than the start
/// of the service: an uptime reading as nothing however long the service had really been up, and
/// only on a machine where nobody had opened the page yet.
/// </para>
/// <para>
/// Not in <c>StartAsync</c>, which was the first attempt and was wrong. The host materialises
/// every hosted service before starting any of them, but it then starts them in registration
/// order -- and <c>GenericWebHostService</c>, which is Kestrel, is registered while the builder
/// is being constructed, long before <c>AddHealth</c>. So a <c>StartAsync</c> stamp happens after
/// the port is already accepting connections, and a request landing in that window would read
/// <see cref="DateTime"/>.MinValue and be rendered as an uptime of some seven hundred thousand
/// days. Construction happens before any of them start, so there is no such window.
/// </para>
/// <para>
/// The clock is injected, like everywhere else here: a service that reads
/// <see cref="DateTime"/>.UtcNow cannot be tested against an uptime that is not the age of the
/// test run.
/// </para>
/// </remarks>
public sealed class ServiceStartTime(TimeProvider clock) : IHostedService
{
    /// <summary>UTC, like every timestamp this API produces.</summary>
    public DateTime StartedAt { get; } = clock.GetUtcNow().UtcDateTime;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

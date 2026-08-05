namespace WindowsControlService.Infrastructure.Hosting;

/// <inheritdoc cref="ISequentialExecutor"/>
public sealed class SequentialExecutor : ISequentialExecutor, IDisposable
{
    private const string ReentrancyMessage =
        "Re-entrant call to ISequentialExecutor. Operations must not nest; move the inner work "
        + "outside the outer RunAsync call.";

    // AsyncLocal rather than a thread-local flag: the continuation after an await can resume
    // on a different thread, and the marker has to follow the logical call, not the thread.
    //
    // Per instance and not static: the marker exists to catch a call that would block on the
    // semaphore this same executor already holds. A static marker is shared by every executor,
    // so nesting a call to one inside another -- two independent semaphores, no possible
    // deadlock -- would be rejected as re-entrancy.
    private readonly AsyncLocal<bool> _inside = new();

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Without this the second WaitAsync would block forever on a semaphore this same call
        // already holds, and the service would go silent with no exception and no log.
        if (_inside.Value)
        {
            throw new InvalidOperationException(ReentrancyMessage);
        }

        await _gate.WaitAsync(cancellationToken);
        _inside.Value = true;
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _inside.Value = false;
            _gate.Release();
        }
    }

    public Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunAsync<object?>(
            async token =>
            {
                await operation(token);
                return null;
            },
            cancellationToken);
    }

    public void Dispose() => _gate.Dispose();
}

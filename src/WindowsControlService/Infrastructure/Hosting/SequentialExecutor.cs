namespace WindowsControlService.Infrastructure.Hosting;

/// <inheritdoc cref="ISequentialExecutor"/>
public sealed class SequentialExecutor : ISequentialExecutor, IDisposable
{
    private const string ReentrancyMessage =
        "Re-entrant call to ISequentialExecutor. Operations must not nest; move the inner work "
        + "outside the outer RunAsync call.";

    // AsyncLocal rather than a thread-local flag: the continuation after an await can resume
    // on a different thread, and the marker has to follow the logical call, not the thread.
    private static readonly AsyncLocal<bool> Inside = new();

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Without this the second WaitAsync would block forever on a semaphore this same call
        // already holds, and the service would go silent with no exception and no log.
        if (Inside.Value)
        {
            throw new InvalidOperationException(ReentrancyMessage);
        }

        await _gate.WaitAsync(cancellationToken);
        Inside.Value = true;
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            Inside.Value = false;
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

namespace WindowsControlService.Infrastructure.Hosting;

/// <summary>
/// Serialises every operation that mutates machine state: applying or removing a WDAC policy,
/// writing the registry. There is one lock for the whole service, not one per component, so
/// "the reconciliation worker never runs while an HTTP request is applying a policy" is
/// structural instead of agreed.
/// </summary>
/// <remarks>
/// Calling into the executor from inside an operation deadlocks. Implementations must detect
/// it and throw instead of hanging.
/// </remarks>
public interface ISequentialExecutor
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

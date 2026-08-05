using WindowsControlService.Infrastructure.Hosting;

namespace WindowsControlService.UnitTests.Infrastructure.Hosting;

public sealed class SequentialExecutorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task OperationsNeverOverlap()
    {
        using var executor = new SequentialExecutor();
        var concurrent = 0;
        var peak = 0;
        var gate = new Lock();

        async Task Operation(CancellationToken token)
        {
            lock (gate)
            {
                concurrent++;
                peak = Math.Max(peak, concurrent);
            }

            await Task.Delay(20, token);

            lock (gate)
            {
                concurrent--;
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => executor.RunAsync(Operation, CancellationToken.None)));

        Assert.Equal(1, peak);
        Assert.Equal(0, concurrent);
    }

    [Fact]
    public async Task ReturnsTheValueOfTheOperation()
    {
        using var executor = new SequentialExecutor();

        var value = await executor.RunAsync(_ => Task.FromResult(7), CancellationToken.None);

        Assert.Equal(7, value);
    }

    [Fact]
    public async Task ReentrantCallThrowsInsteadOfHanging()
    {
        using var executor = new SequentialExecutor();

        // The timeout is the point of this test: if re-entrancy is not detected the inner call
        // blocks forever and would otherwise hang the whole test run.
        var attempt = executor.RunAsync(
            async _ => await executor.RunAsync(_ => Task.FromResult(1), CancellationToken.None),
            CancellationToken.None);

        var finished = await Task.WhenAny(attempt, Task.Delay(TestTimeout));
        Assert.Same(attempt, finished);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => attempt);
        Assert.Contains("Re-entrant", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NestingTwoExecutorsIsNotReentrancy()
    {
        using var outer = new SequentialExecutor();
        using var inner = new SequentialExecutor();

        // Two executors are two semaphores, so the inner call cannot block on a lock the outer
        // one holds. Rejecting it would be a false positive.
        var value = await outer.RunAsync(
            async _ => await inner.RunAsync(_ => Task.FromResult(3), CancellationToken.None),
            CancellationToken.None);

        Assert.Equal(3, value);
    }

    [Fact]
    public async Task ReleasesTheGateWhenAnOperationThrows()
    {
        using var executor = new SequentialExecutor();

        await Assert.ThrowsAsync<InvalidTimeZoneException>(() =>
            executor.RunAsync<int>(_ => throw new InvalidTimeZoneException(), CancellationToken.None));

        // If the finally block did not run, this would block forever.
        var next = executor.RunAsync(_ => Task.FromResult(1), CancellationToken.None);
        var finished = await Task.WhenAny(next, Task.Delay(TestTimeout));

        Assert.Same(next, finished);
        Assert.Equal(1, await next);
    }

    [Fact]
    public async Task CancellationIsPropagatedToWaitingCallers()
    {
        using var executor = new SequentialExecutor();
        using var occupied = new SemaphoreSlim(0, 1);
        using var release = new SemaphoreSlim(0, 1);
        using var cancellation = new CancellationTokenSource();

        var holder = executor.RunAsync(
            async _ =>
            {
                occupied.Release();
                await release.WaitAsync(CancellationToken.None);
            },
            CancellationToken.None);

        await occupied.WaitAsync(TestTimeout);

        var queued = executor.RunAsync(_ => Task.CompletedTask, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        release.Release();
        await holder;
    }

    [Fact]
    public async Task CancellationReachesTheOperationItself()
    {
        using var executor = new SequentialExecutor();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.RunAsync(token => Task.FromCanceled(token), cancellation.Token));
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsControlService.Features.ApplicationBlocking;
using WindowsControlService.Infrastructure.Events;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Features.ApplicationBlocking;

public sealed class PolicyReconciliationWorkerTests
{
    [Fact]
    public async Task TheFirstCycleRunsAtStartupNotAnIntervalLater()
    {
        var blocking = new CountingBlockingService();

        // An hour: if the worker used a plain while loop instead of do/while, nothing would
        // happen here and the test would time out.
        using var worker = BuildWorker(blocking, TimeSpan.FromHours(1));

        await worker.StartAsync(CancellationToken.None);
        await blocking.WaitForCyclesAsync(1);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(blocking.Cycles >= 1);
    }

    [Fact]
    public async Task ItKeepsGoingAfterACycleThrows()
    {
        var blocking = new CountingBlockingService { Throw = true };
        using var worker = BuildWorker(blocking, TimeSpan.FromMilliseconds(50));

        await worker.StartAsync(CancellationToken.None);
        await blocking.WaitForCyclesAsync(3);
        await worker.StopAsync(CancellationToken.None);

        // A failing cycle must never take the worker down: after it dies, nothing watches the
        // policy again until the service restarts.
        Assert.True(blocking.Cycles >= 3);
    }

    [Fact]
    public async Task ItKeepsGoingAfterACycleReportsAFailure()
    {
        var blocking = new CountingBlockingService
        {
            Failure = new Error(ErrorCode.PlatformUnavailable, "CiTool is not here."),
        };

        using var worker = BuildWorker(blocking, TimeSpan.FromMilliseconds(50));

        await worker.StartAsync(CancellationToken.None);
        await blocking.WaitForCyclesAsync(3);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(blocking.Cycles >= 3);
    }

    private static PolicyReconciliationWorker BuildWorker(IApplicationBlockingService blocking, TimeSpan interval) =>
        new(
            blocking,
            new SilentEventBroadcaster(),
            Options.Create(new ApplicationBlockingOptions { ReconciliationInterval = interval }),
            NullLogger<PolicyReconciliationWorker>.Instance);

    /// <summary>Reports no listeners, so the worker skips the publishing branch entirely.</summary>
    private sealed class SilentEventBroadcaster : IServiceEventBroadcaster
    {
        public bool HasSubscribers => false;

        public void Publish(ServiceEvent serviceEvent) =>
            throw new InvalidOperationException("Nothing should be published with no subscribers.");

        public IServiceEventSubscription Subscribe() => throw new NotSupportedException();
    }

    private sealed class CountingBlockingService : IApplicationBlockingService
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _target = int.MaxValue;
        private int _cycles;

        /// <summary>Written by the worker's thread and read by the test's, so never a plain field.</summary>
        public int Cycles => Volatile.Read(ref _cycles);

        public bool Throw { get; init; }

        public Error? Failure { get; init; }

        public async Task WaitForCyclesAsync(int count)
        {
            Volatile.Write(ref _target, count);

            // The worker starts before this is called, so the cycles being waited for may already
            // have happened -- and then nothing would ever set the signal. Checking after writing
            // the target closes that window from this side; the worker closes it from the other.
            if (Cycles >= count)
            {
                _reached.TrySetResult();
            }

            var finished = await Task.WhenAny(_reached.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(_reached.Task, finished);
        }

        public Task<Result> ReconcileAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _cycles);
            if (Cycles >= Volatile.Read(ref _target))
            {
                _reached.TrySetResult();
            }

            if (Throw)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(Failure is { } error ? Result.Failure(error) : Result.Success());
        }

        public Task<IReadOnlyList<BlockedApplication>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BlockedApplication>>([]);

        public Task<Result<BlockedApplication>> GetByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(Result<BlockedApplication>.Failure(ErrorCode.NotFound, "no"));

        public Task<Result<long>> AddAsync(string executablePath, string name, CancellationToken cancellationToken) =>
            Task.FromResult(Result<long>.Failure(ErrorCode.NotFound, "no"));

        public Task<Result> RemoveAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure(ErrorCode.NotFound, "no"));

        public Task<Result> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure(ErrorCode.NotFound, "no"));

        public Task<Result<PolicyStateResponse>> GetPolicyStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<PolicyStateResponse>.Success(new PolicyStateResponse(PolicyState.Unknown, 0, null)));
    }
}

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace WindowsControlService.Infrastructure.Events;

public interface IServiceEventSubscription : IDisposable
{
    /// <summary>
    /// The next event, or <see langword="null"/> when the subscription ends. Cancellation is an
    /// ordinary ending here, not an error: a stream that reached its lifetime or a browser that
    /// navigated away are both normal, and turning them into exceptions would fill the log with
    /// nothing.
    /// </summary>
    ValueTask<ServiceEvent?> ReadAsync(CancellationToken cancellationToken);
}

public interface IServiceEventBroadcaster
{
    /// <summary>
    /// Whether anyone is listening. Publishers use it to skip work that only exists to feed the
    /// stream: asking CiTool for the policy state every minute costs a process, and with no
    /// browser open nobody would read the answer.
    /// </summary>
    bool HasSubscribers { get; }

    void Publish(ServiceEvent serviceEvent);

    IServiceEventSubscription Subscribe();
}

public sealed class ServiceEventBroadcaster(IOptions<ServiceEventOptions> options) : IServiceEventBroadcaster
{
    private readonly ConcurrentDictionary<Subscription, byte> _subscriptions = new();

    public bool HasSubscribers => !_subscriptions.IsEmpty;

    public void Publish(ServiceEvent serviceEvent)
    {
        ArgumentNullException.ThrowIfNull(serviceEvent);

        foreach (var subscription in _subscriptions.Keys)
        {
            subscription.Write(serviceEvent);
        }
    }

    public IServiceEventSubscription Subscribe()
    {
        var subscription = new Subscription(this, options.Value.SubscriberQueueCapacity);
        _subscriptions[subscription] = 0;

        return subscription;
    }

    private void Unsubscribe(Subscription subscription) => _subscriptions.TryRemove(subscription, out _);

    private sealed class Subscription : IServiceEventSubscription
    {
        private readonly ServiceEventBroadcaster _owner;
        private readonly Channel<ServiceEvent> _channel;

        public Subscription(ServiceEventBroadcaster owner, int capacity)
        {
            _owner = owner;

            // Bounded and dropping, never blocking: a reader that stalls must not be able to
            // slow down the reconciliation worker that is publishing to it. Every event carries
            // the full current value, so the newest one is the one that matters.
            _channel = Channel.CreateBounded<ServiceEvent>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
        }

        public void Write(ServiceEvent serviceEvent) => _channel.Writer.TryWrite(serviceEvent);

        public async ValueTask<ServiceEvent?> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _channel.Reader.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            _owner.Unsubscribe(this);
            _channel.Writer.TryComplete();
        }
    }
}

using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Events;

namespace WindowsControlService.UnitTests.Infrastructure.Events;

public sealed class ServiceEventBroadcasterTests
{
    [Fact]
    public void ThereAreSubscribersOnlyWhileASubscriptionIsOpen()
    {
        // The reconciliation worker skips a CiTool call when nobody is listening, so a
        // subscription that outlived its stream would cost a process every cycle for nothing.
        var broadcaster = Broadcaster();
        Assert.False(broadcaster.HasSubscribers);

        var subscription = broadcaster.Subscribe();
        Assert.True(broadcaster.HasSubscribers);

        subscription.Dispose();
        Assert.False(broadcaster.HasSubscribers);
    }

    [Fact]
    public async Task EverySubscriberGetsEveryEvent()
    {
        var broadcaster = Broadcaster();
        using var first = broadcaster.Subscribe();
        using var second = broadcaster.Subscribe();

        broadcaster.Publish(new ServiceEvent("usb", true));

        Assert.Equal("usb", (await first.ReadAsync(CancellationToken.None))?.Name);
        Assert.Equal("usb", (await second.ReadAsync(CancellationToken.None))?.Name);
    }

    [Fact]
    public async Task AReaderThatFallsBehindKeepsTheNewestEvents()
    {
        // Every event carries the whole current value, so dropping the oldest loses nothing a
        // reader needs, and publishing never waits on a stalled browser.
        var broadcaster = Broadcaster(capacity: 2);
        using var subscription = broadcaster.Subscribe();

        broadcaster.Publish(new ServiceEvent("usb", 1));
        broadcaster.Publish(new ServiceEvent("usb", 2));
        broadcaster.Publish(new ServiceEvent("usb", 3));

        Assert.Equal(2, (await subscription.ReadAsync(CancellationToken.None))?.Payload);
        Assert.Equal(3, (await subscription.ReadAsync(CancellationToken.None))?.Payload);
    }

    private static ServiceEventBroadcaster Broadcaster(int capacity = 32) =>
        new(Options.Create(new ServiceEventOptions { SubscriberQueueCapacity = capacity }));
}

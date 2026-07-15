using H3xBoardServer.Services.Sharing;

namespace H3xBoardServer.Tests;

public class InMemoryShareBusTests
{
    [Fact]
    public async Task Publish_ReachesAllSubscribersOfTheChannel_InOrder()
    {
        var bus = new InMemoryShareBus();
        var first = new List<string>();
        var second = new List<string>();
        await bus.SubscribeAsync("channel-a", first.Add);
        await bus.SubscribeAsync("channel-a", second.Add);

        await bus.PublishAsync("channel-a", "frame-1");
        await bus.PublishAsync("channel-a", "frame-2");

        Assert.Equal(["frame-1", "frame-2"], first);
        Assert.Equal(["frame-1", "frame-2"], second);
    }

    [Fact]
    public async Task Publish_DoesNotCrossChannels()
    {
        var bus = new InMemoryShareBus();
        var received = new List<string>();
        await bus.SubscribeAsync("channel-a", received.Add);

        await bus.PublishAsync("channel-b", "other");
        Assert.Empty(received);
    }

    [Fact]
    public async Task Publish_WithoutSubscribers_IsANoOp()
    {
        var bus = new InMemoryShareBus();
        await bus.PublishAsync("nobody-home", "frame");  // must not throw
    }

    [Fact]
    public async Task DisposedSubscription_StopsReceiving()
    {
        var bus = new InMemoryShareBus();
        var kept = new List<string>();
        var dropped = new List<string>();
        await bus.SubscribeAsync("channel-a", kept.Add);
        var subscription = await bus.SubscribeAsync("channel-a", dropped.Add);

        await subscription.DisposeAsync();
        await subscription.DisposeAsync();  // double dispose is safe
        await bus.PublishAsync("channel-a", "frame");

        Assert.Equal(["frame"], kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public async Task ThrowingHandler_DoesNotBreakTheFanOut()
    {
        var bus = new InMemoryShareBus();
        var received = new List<string>();
        await bus.SubscribeAsync("channel-a", _ => throw new InvalidOperationException("boom"));
        await bus.SubscribeAsync("channel-a", received.Add);

        await bus.PublishAsync("channel-a", "frame");
        Assert.Equal(["frame"], received);
    }
}

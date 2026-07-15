using StackExchange.Redis;

namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Redis pub/sub <see cref="IShareBus"/>. One <see cref="ChannelMessageQueue"/> per channel per
/// instance (refcounted) — <c>OnMessage</c> processes messages sequentially, preserving publish
/// order, and dispatches to every local handler.
/// </summary>
public class RedisShareBus(IConnectionMultiplexer redis, ILogger<RedisShareBus> logger) : IShareBus
{
    private sealed class ChannelEntry
    {
        public Dictionary<Guid, Action<string>> Handlers { get; } = [];
        public ChannelMessageQueue? Queue { get; set; }
    }

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Dictionary<string, ChannelEntry> _entries = [];

    public async Task PublishAsync(string channel, string frame)
        => await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), frame);

    public async Task<IAsyncDisposable> SubscribeAsync(string channel, Action<string> handler)
    {
        var id = Guid.NewGuid();
        await _mutex.WaitAsync();
        try
        {
            if (!_entries.TryGetValue(channel, out var entry))
            {
                entry = new ChannelEntry();
                _entries[channel] = entry;
                var queue = await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(channel));
                queue.OnMessage(message => Dispatch(channel, (string?)message.Message));
                entry.Queue = queue;
            }

            lock (entry.Handlers)
                entry.Handlers[id] = handler;
        }
        finally
        {
            _mutex.Release();
        }

        return new Subscription(this, channel, id);
    }

    private void Dispatch(string channel, string? frame)
    {
        if (frame is null)
            return;
        if (!_entries.TryGetValue(channel, out var entry))
            return;

        Action<string>[] handlers;
        lock (entry.Handlers)
            handlers = [.. entry.Handlers.Values];

        foreach (var handler in handlers)
        {
            try
            {
                handler(frame);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Share bus handler failed for channel {Channel}", channel);
            }
        }
    }

    private async Task UnsubscribeAsync(string channel, Guid id)
    {
        ChannelMessageQueue? queueToClose = null;
        await _mutex.WaitAsync();
        try
        {
            if (!_entries.TryGetValue(channel, out var entry))
                return;

            bool empty;
            lock (entry.Handlers)
            {
                entry.Handlers.Remove(id);
                empty = entry.Handlers.Count == 0;
            }

            if (empty)
            {
                _entries.Remove(channel);
                queueToClose = entry.Queue;
            }
        }
        finally
        {
            _mutex.Release();
        }

        if (queueToClose is not null)
            await queueToClose.UnsubscribeAsync();
    }

    private sealed class Subscription(RedisShareBus bus, string channel, Guid id) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;
            await bus.UnsubscribeAsync(channel, id);
        }
    }
}

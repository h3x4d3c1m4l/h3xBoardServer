using System.Collections.Concurrent;

namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// In-process <see cref="IShareBus"/> fallback for when Redis is not configured. Single-instance
/// only — frames never leave this process. Handlers run inline on the publisher's thread, so
/// publish order is preserved per publisher.
/// </summary>
public class InMemoryShareBus : IShareBus
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Action<string>>> _channels = new();

    public Task PublishAsync(string channel, string frame)
    {
        if (_channels.TryGetValue(channel, out var handlers))
        {
            foreach (var handler in handlers.Values)
            {
                try
                {
                    handler(frame);
                }
                catch
                {
                    // Handlers own their error handling; a bad one must not break the fan-out.
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IAsyncDisposable> SubscribeAsync(string channel, Action<string> handler)
    {
        var id = Guid.NewGuid();
        _channels.GetOrAdd(channel, _ => new ConcurrentDictionary<Guid, Action<string>>())[id] = handler;
        return Task.FromResult<IAsyncDisposable>(new Subscription(this, channel, id));
    }

    private void Unsubscribe(string channel, Guid id)
    {
        if (!_channels.TryGetValue(channel, out var handlers))
            return;
        handlers.TryRemove(id, out _);
        if (handlers.IsEmpty)
            _channels.TryRemove(channel, out _);
    }

    private sealed class Subscription(InMemoryShareBus bus, string channel, Guid id) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                bus.Unsubscribe(channel, id);
            return ValueTask.CompletedTask;
        }
    }
}

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Per-instance map of share code → local viewer sockets. Holds one refcounted bus subscription per
/// code and forwards every data-channel frame to each local viewer through a bounded queue — a
/// viewer that cannot keep up (full queue) is dropped so one slow socket can never stall the relay.
/// A per-code watchdog polls the store and ends local viewers when the session key has expired.
/// Registered as a singleton.
/// </summary>
public class ViewerRegistry : IAsyncDisposable
{
    private const int DefaultQueueCapacity = 256;
    private static readonly TimeSpan DefaultWatchdogInterval = TimeSpan.FromSeconds(10);

    private sealed class CodeEntry(string code)
    {
        public string Code { get; } = code;
        public ConcurrentDictionary<string, ViewerHandle> Viewers { get; } = new();
        public IAsyncDisposable? Subscription { get; set; }
        public CancellationTokenSource WatchdogCts { get; } = new();
    }

    private readonly IShareBus _bus;
    private readonly IShareStore _store;
    private readonly ILogger<ViewerRegistry> _logger;
    private readonly int _queueCapacity;
    private readonly TimeSpan _watchdogInterval;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly ConcurrentDictionary<string, CodeEntry> _codes = new();

    public ViewerRegistry(IShareBus bus, IShareStore store, ILogger<ViewerRegistry> logger)
        : this(bus, store, logger, DefaultQueueCapacity, DefaultWatchdogInterval)
    {
    }

    internal ViewerRegistry(IShareBus bus, IShareStore store, ILogger<ViewerRegistry> logger, int queueCapacity, TimeSpan watchdogInterval)
    {
        _bus = bus;
        _store = store;
        _logger = logger;
        _queueCapacity = queueCapacity;
        _watchdogInterval = watchdogInterval;
    }

    /// <summary>
    /// Registers a local viewer for <paramref name="code"/>, subscribing to the code's data channel
    /// and starting its watchdog when this is the first local viewer.
    /// </summary>
    public async Task<ViewerHandle> RegisterAsync(string code, string viewerId)
    {
        await _mutex.WaitAsync();
        try
        {
            if (!_codes.TryGetValue(code, out var entry))
            {
                entry = new CodeEntry(code);
                _codes[code] = entry;
                entry.Subscription = await _bus.SubscribeAsync(ShareKeys.DataChannel(code), frame => OnDataFrame(entry, frame));
                _ = WatchdogAsync(entry, entry.WatchdogCts.Token);
            }

            var handle = new ViewerHandle(viewerId, _queueCapacity);
            entry.Viewers[viewerId] = handle;
            return handle;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>Removes a local viewer; drops the subscription + watchdog when it was the last one.</summary>
    public async Task UnregisterAsync(string code, ViewerHandle handle)
    {
        IAsyncDisposable? subscription = null;
        await _mutex.WaitAsync();
        try
        {
            if (!_codes.TryGetValue(code, out var entry))
                return;

            entry.Viewers.TryRemove(handle.ViewerId, out _);
            if (!entry.Viewers.IsEmpty)
                return;

            _codes.TryRemove(code, out _);
            entry.WatchdogCts.Cancel();
            subscription = entry.Subscription;
        }
        finally
        {
            _mutex.Release();
        }

        if (subscription is not null)
            await subscription.DisposeAsync();
    }

    /// <summary>
    /// Bus handler — must not block. Fans the frame out to every local viewer's bounded queue;
    /// a full queue means the viewer cannot keep up, so it is failed and dropped on the spot.
    /// </summary>
    private void OnDataFrame(CodeEntry entry, string frame)
    {
        foreach (var (viewerId, handle) in entry.Viewers)
        {
            if (handle.TryEnqueue(frame))
                continue;

            _logger.LogInformation("Share viewer {ViewerId} on session {Code} overflowed its queue — dropping it", viewerId, entry.Code);
            handle.Fail();
            entry.Viewers.TryRemove(viewerId, out _);
        }

        // The session was ended by its presenter — flush the ended frame (enqueued above) and
        // close the local viewers. Cleanup is async, so hand it off.
        if (ServerFrames.IsSessionEnded(frame))
            _ = EndEntryAsync(entry, endedFrame: null);
    }

    private async Task WatchdogAsync(CodeEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_watchdogInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                bool sessionExists;
                try
                {
                    sessionExists = await _store.SessionExistsAsync(entry.Code);
                }
                catch (Exception ex)
                {
                    // A failing store (e.g. Redis restarting) says nothing about the session:
                    // keep the viewers and keep polling — the next successful check settles it
                    // either way. Exiting here would leave expiry undetected for this code.
                    _logger.LogWarning(ex, "Share session watchdog check failed for {Code} — retrying", entry.Code);
                    continue;
                }

                if (sessionExists)
                    continue;

                _logger.LogInformation("Share session {Code} expired — closing {Count} local viewer(s)", entry.Code, entry.Viewers.Count);
                await EndEntryAsync(entry, ServerFrames.SessionEnded(SessionEndReasons.Expired));
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Last local viewer left — normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Share session watchdog failed for {Code}", entry.Code);
        }
    }

    private async Task EndEntryAsync(CodeEntry entry, string? endedFrame)
    {
        IAsyncDisposable? subscription = null;
        await _mutex.WaitAsync();
        try
        {
            // Only clean up if this entry is still the registered one (a viewer may have re-joined).
            if (_codes.TryGetValue(entry.Code, out var current) && ReferenceEquals(current, entry))
            {
                _codes.TryRemove(entry.Code, out _);
                entry.WatchdogCts.Cancel();
                subscription = entry.Subscription;
            }

            foreach (var handle in entry.Viewers.Values)
            {
                if (endedFrame is not null)
                    handle.TryEnqueue(endedFrame);
                handle.Complete();
            }
            entry.Viewers.Clear();
        }
        finally
        {
            _mutex.Release();
        }

        if (subscription is not null)
            await subscription.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _codes.Values)
            await EndEntryAsync(entry, null);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// One local viewer's relay queue. The viewer WebSocket handler drains <see cref="Frames"/> and
/// closes the socket when the reader completes — normally (session ended) or because the viewer
/// was too slow (<see cref="Overflowed"/>).
/// </summary>
public sealed class ViewerHandle(string viewerId, int capacity)
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,  // TryWrite returns false when full — never blocks
    });

    public string ViewerId { get; } = viewerId;

    public ChannelReader<string> Frames => _channel.Reader;

    /// <summary>True when the viewer was dropped because it could not keep up with the relay.</summary>
    public bool Overflowed { get; private set; }

    public bool TryEnqueue(string frame) => _channel.Writer.TryWrite(frame);

    public void Fail()
    {
        Overflowed = true;
        _channel.Writer.TryComplete();
    }

    public void Complete() => _channel.Writer.TryComplete();
}

namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Forwards viewer-triggered events to the presenter as JSON-RPC notifications on its WebSocket
/// connection. Viewer events arrive as internal frames on the session's control channel (published
/// by whichever instance holds the viewer socket — the presenter may be connected to a different
/// instance), get debounced here, and go out via <see cref="RpcConnection.JsonRpc"/>:
/// <c>sharing.v1.viewerCount</c>, <c>sharing.v1.snapshotRequested</c>, <c>sharing.v1.ended</c>.
/// Scoped to the presenter's WebSocket connection.
/// </summary>
public class PresenterNotifier(RpcConnection connection, IShareBus bus, ILogger<PresenterNotifier> logger) : IAsyncDisposable
{
    private static readonly TimeSpan ViewerCountDebounce = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SnapshotRequestDebounce = TimeSpan.FromMilliseconds(250);

    private IAsyncDisposable? _subscription;
    private Debouncer? _viewerCountDebouncer;
    private Debouncer? _snapshotDebouncer;
    private volatile int _lastViewerCount;

    /// <summary>Starts listening on the control channel for <paramref name="code"/>.</summary>
    public async Task AttachAsync(string code)
    {
        await DetachAsync();
        _viewerCountDebouncer = new Debouncer(ViewerCountDebounce,
            () => NotifyAsync("sharing.v1.viewerCount", new ViewerCountNotification(_lastViewerCount)));
        _snapshotDebouncer = new Debouncer(SnapshotRequestDebounce,
            () => NotifyAsync("sharing.v1.snapshotRequested", null));
        _subscription = await bus.SubscribeAsync(ShareKeys.ControlChannel(code), OnControlFrame);
    }

    public async Task DetachAsync()
    {
        _viewerCountDebouncer?.Dispose();
        _viewerCountDebouncer = null;
        _snapshotDebouncer?.Dispose();
        _snapshotDebouncer = null;
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
            _subscription = null;
        }
    }

    /// <summary>Tells the presenter its session is gone (not debounced — it happens at most once).</summary>
    public Task NotifyEndedAsync(string reason)
        => NotifyAsync("sharing.v1.ended", new SessionEndedNotification(reason));

    private void OnControlFrame(string frame)
    {
        try
        {
            using var document = JsonDocument.Parse(frame);
            if (!document.RootElement.TryGetProperty("type", out var typeProperty))
                return;

            switch (typeProperty.GetString())
            {
                case ControlFrames.ViewerCountType:
                    if (document.RootElement.TryGetProperty("count", out var countProperty)
                        && countProperty.TryGetInt32(out var count))
                    {
                        _lastViewerCount = count;
                        _viewerCountDebouncer?.Signal();
                    }
                    break;

                case ControlFrames.SnapshotRequestedType:
                    _snapshotDebouncer?.Signal();
                    break;
            }
        }
        catch (JsonException)
        {
            // Control frames are server-generated; anything unparsable is safely ignored.
        }
    }

    private async Task NotifyAsync(string method, object? argument)
    {
        var jsonRpc = connection.JsonRpc;
        if (jsonRpc is null)
            return;

        try
        {
            await jsonRpc.NotifyWithParameterObjectAsync(method, argument);
        }
        catch (Exception ex)
        {
            // The connection may be tearing down — a lost notification is fine.
            logger.LogDebug(ex, "Failed to send {Method} notification to presenter", method);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DetachAsync();
        GC.SuppressFinalize(this);
    }
}

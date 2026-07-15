using System.Net.WebSockets;
using System.Text;
using H3xBoardServer.Services.Sharing;

namespace H3xBoardServer.Rpc;

/// <summary>
/// The anonymous viewer WebSocket — <c>/ws/v1/view/{code}</c>. Unlike <c>/ws/v1</c> this is plain
/// JSON frames, not JSON-RPC, and deliberately has <b>no</b> session auth gate: the ephemeral share
/// code is the credential (rate-limited per IP against brute force). A viewer receives a
/// server-origin <c>hello</c>, the cached snapshot (if any), and then every relayed frame from the
/// session's data channel. Inbound frames are capped at 1 KB and only <c>ping</c> and
/// <c>resyncRequest</c> are understood — everything else is ignored. See <c>docs/live-sharing.md</c>.
/// </summary>
public static class ViewWsEndpoints
{
    private const int MaxInboundFrameBytes = 1024;
    private static readonly TimeSpan ResyncMinInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    public static WebApplication MapViewWsEndpoints(this WebApplication app)
    {
        app.Map("/ws/v1/view/{code}", HandleView);
        return app;
    }

    private static async Task HandleView(
        HttpContext httpContext,
        string code,
        IShareStore store,
        IShareBus bus,
        ViewerRegistry registry,
        ShareCodeRateLimiter rateLimiter,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(nameof(ViewWsEndpoints));
        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("WebSocket connection required");
            return;
        }

        // Anonymous endpoint ⇒ the code is the credential; rate-limit lookups per IP.
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimiter.TryAcquire(remoteIp))
        {
            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await httpContext.Response.WriteAsync("Too many attempts — try again later");
            return;
        }

        var normalizedCode = ShareCodes.Normalize(code);
        var session = ShareCodes.IsValid(normalizedCode) ? await store.GetSessionAsync(normalizedCode) : null;

        var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();

        if (session is null)
        {
            await SendAsync(webSocket, ServerFrames.Hello(HelloStates.NotFound));
            await TryCloseAsync(webSocket, WebSocketCloseStatus.NormalClosure, "Unknown share code", logger);
            return;
        }

        var maxViewers = configuration.GetValue("Sharing:MaxViewersPerSession", 100);
        if (await store.GetViewerCountAsync(normalizedCode) >= maxViewers)
        {
            await SendAsync(webSocket, ServerFrames.Hello(HelloStates.Full));
            await TryCloseAsync(webSocket, WebSocketCloseStatus.NormalClosure, "Session is full", logger);
            return;
        }

        var ttl = TimeSpan.FromSeconds(configuration.GetValue("Sharing:SessionTtlSeconds", 180));
        var viewerId = Guid.NewGuid().ToString();
        var handle = await registry.RegisterAsync(normalizedCode, viewerId);
        logger.LogInformation("Share viewer {ViewerId} joined session {Code} from {Ip}", viewerId, normalizedCode, remoteIp);

        try
        {
            await store.AddViewerAsync(normalizedCode, viewerId, ttl);
            await PublishViewerCountAsync(store, bus, normalizedCode, includeDataChannel: true);
            // A joining viewer wants a fresh snapshot — ask the presenter (debounced on its side).
            await bus.PublishAsync(ShareKeys.ControlChannel(normalizedCode), ControlFrames.SnapshotRequested());

            // Re-read now that we are subscribed, so no frame between lookup and subscribe is lost —
            // the snapshot brings us up to date and the queued relay continues from there.
            session = await store.GetSessionAsync(normalizedCode);
            if (session is null)
            {
                await SendAsync(webSocket, ServerFrames.SessionEnded(SessionEndReasons.Expired));
                await TryCloseAsync(webSocket, WebSocketCloseStatus.NormalClosure, "Session ended", logger);
                return;
            }

            var state = session.State == ShareSessionStates.Paused || session.SnapshotJson is null
                ? HelloStates.Waiting
                : HelloStates.Live;
            await SendAsync(webSocket, ServerFrames.Hello(state));
            if (session.SnapshotJson is not null)
                await SendAsync(webSocket, session.SnapshotJson);

            await PumpAsync(webSocket, handle, store, bus, normalizedCode, viewerId, ttl, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Share viewer {ViewerId} on session {Code} errored", viewerId, normalizedCode);
        }
        finally
        {
            await registry.UnregisterAsync(normalizedCode, handle);
            await store.RemoveViewerAsync(normalizedCode, viewerId);
            try
            {
                await PublishViewerCountAsync(store, bus, normalizedCode, includeDataChannel: true);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to publish viewer count after {ViewerId} left {Code}", viewerId, normalizedCode);
            }
            logger.LogInformation("Share viewer {ViewerId} left session {Code}", viewerId, normalizedCode);
        }
    }

    /// <summary>Runs the send pump (relay queue → socket) and the read loop until either ends.</summary>
    private static async Task PumpAsync(
        WebSocket webSocket, ViewerHandle handle, IShareStore store, IShareBus bus,
        string code, string viewerId, TimeSpan ttl, ILogger logger)
    {
        using var sendCts = new CancellationTokenSource();
        var sendTask = SendLoopAsync(webSocket, handle, sendCts.Token);
        var receiveTask = ReceiveLoopAsync(webSocket, store, bus, code, viewerId, ttl, logger);

        var first = await Task.WhenAny(sendTask, receiveTask);
        if (first == sendTask)
        {
            // Relay queue completed: the session ended, or this viewer overflowed its queue.
            var (status, description) = handle.Overflowed
                ? (WebSocketCloseStatus.InternalServerError, "Viewer cannot keep up")
                : (WebSocketCloseStatus.NormalClosure, "Session ended");
            await TryCloseOutputAsync(webSocket, status, description, logger);
            await WaitBrieflyAsync(receiveTask);  // give the client a moment to ack the close
        }
        else
        {
            // Client closed (or the socket faulted) — stop the pump and complete the handshake.
            sendCts.Cancel();
            await WaitBrieflyAsync(sendTask);
            await TryCloseOutputAsync(webSocket, WebSocketCloseStatus.NormalClosure, "Bye", logger);
        }
    }

    private static async Task SendLoopAsync(WebSocket webSocket, ViewerHandle handle, CancellationToken cancellationToken)
    {
        await foreach (var frame in handle.Frames.ReadAllAsync(cancellationToken))
            await SendAsync(webSocket, frame, cancellationToken);
    }

    /// <summary>
    /// Reads viewer frames until the client closes. Understands <c>{"type":"ping"}</c> (presence
    /// refresh) and <c>{"type":"resyncRequest"}</c> (snapshot re-request, min 1 per 5 s per socket);
    /// anything else — including unparsable JSON — is ignored. Frames over 1 KB close the socket.
    /// </summary>
    private static async Task ReceiveLoopAsync(
        WebSocket webSocket, IShareStore store, IShareBus bus,
        string code, string viewerId, TimeSpan ttl, ILogger logger)
    {
        var buffer = new byte[MaxInboundFrameBytes];
        var lastResync = DateTimeOffset.MinValue;

        while (webSocket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }
            catch (WebSocketException)
            {
                return;  // socket faulted / client vanished
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return;

            if (!result.EndOfMessage)
            {
                // The frame exceeds our buffer — viewers only ever send tiny control frames.
                await TryCloseOutputAsync(webSocket, WebSocketCloseStatus.MessageTooBig, "Frame too large", logger);
                return;
            }

            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            switch (ParseFrameType(buffer.AsSpan(0, result.Count)))
            {
                case "ping":
                    await store.AddViewerAsync(code, viewerId, ttl);
                    await PublishViewerCountAsync(store, bus, code, includeDataChannel: false);
                    break;

                case "resyncRequest":
                    var now = DateTimeOffset.UtcNow;
                    if (now - lastResync >= ResyncMinInterval)
                    {
                        lastResync = now;
                        await bus.PublishAsync(ShareKeys.ControlChannel(code), ControlFrames.SnapshotRequested());
                    }
                    break;
            }
        }
    }

    private static string? ParseFrameType(ReadOnlySpan<byte> payload)
    {
        try
        {
            var reader = new Utf8JsonReader(payload);
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                    ? type.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;  // ignored, per protocol
        }
    }

    /// <summary>
    /// Publishes the current viewer count: always to the control channel (for the presenter's
    /// debounced notification) and, on join/leave, also to the data channel so viewers see it.
    /// </summary>
    private static async Task PublishViewerCountAsync(IShareStore store, IShareBus bus, string code, bool includeDataChannel)
    {
        var count = await store.GetViewerCountAsync(code);
        await bus.PublishAsync(ShareKeys.ControlChannel(code), ControlFrames.ViewerCount(count));
        if (includeDataChannel)
            await bus.PublishAsync(ShareKeys.DataChannel(code), ServerFrames.ViewerCount(count));
    }

    private static ValueTask SendAsync(WebSocket webSocket, string frame, CancellationToken cancellationToken = default)
        => webSocket.SendAsync(Encoding.UTF8.GetBytes(frame).AsMemory(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    /// <summary>Full close handshake — used on paths where no concurrent receive is pending.</summary>
    private static async Task TryCloseAsync(WebSocket webSocket, WebSocketCloseStatus status, string description, ILogger logger)
    {
        try
        {
            using var cts = new CancellationTokenSource(CloseTimeout);
            await webSocket.CloseAsync(status, description, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Viewer socket close failed");
        }
    }

    /// <summary>Fire-the-close-frame-only variant — safe while a receive loop is still draining.</summary>
    private static async Task TryCloseOutputAsync(WebSocket webSocket, WebSocketCloseStatus status, string description, ILogger logger)
    {
        if (webSocket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return;
        try
        {
            using var cts = new CancellationTokenSource(CloseTimeout);
            await webSocket.CloseOutputAsync(status, description, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Viewer socket close failed");
        }
    }

    private static async Task WaitBrieflyAsync(Task task)
    {
        try
        {
            await task.WaitAsync(CloseTimeout);
        }
        catch
        {
            // Timeout or cancellation during teardown — the socket is being torn down regardless.
        }
    }
}

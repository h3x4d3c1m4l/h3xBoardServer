using System.Text;

namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Presenter-side orchestration for live sharing: session lifecycle (start/resume/stop/pause),
/// the publish hot path, and heartbeats. Scoped to the presenter's WebSocket connection — the
/// active share code lives on <see cref="RpcConnection"/> so a connection has at most one live
/// session. The server never parses board content: published messages are validated as envelopes
/// (<c>type</c>/<c>seq</c>, plus <c>fileIds</c> for snapshots) and relayed verbatim.
/// See <c>docs/live-sharing.md</c>.
/// </summary>
public class ShareSessionService(
    IShareStore store,
    IShareBus bus,
    PresenterNotifier notifier,
    RpcContext context,
    RpcConnection connection,
    IConfiguration configuration,
    ILogger<ShareSessionService> logger)
{
    private const int CodeClaimAttempts = 5;
    private const int DefaultSessionTtlSeconds = 180;
    private const long DefaultMaxMessageBytes = 512 * 1024;

    private TimeSpan SessionTtl
        => TimeSpan.FromSeconds(configuration.GetValue("Sharing:SessionTtlSeconds", DefaultSessionTtlSeconds));

    /// <summary>
    /// Starts (or returns, or resumes) a share session. Idempotent per connection: when this
    /// connection already has a live session it is returned as-is. When <paramref name="resumeCode"/>
    /// names an existing session owned by the same user (e.g. after an app restart within the TTL
    /// grace window), it is re-bound to this connection and set live again. Otherwise a fresh code
    /// is claimed.
    /// </summary>
    public async Task<ShareSessionDto> StartAsync(string? resumeCode)
    {
        var userId = context.UserId!;

        // Idempotent: this connection already has a session — return it if it still exists.
        if (connection.ShareCode is not null)
        {
            var existing = await store.GetSessionAsync(connection.ShareCode);
            if (existing is not null)
                return new ShareSessionDto(existing.Code, await store.GetViewerCountAsync(existing.Code));

            // It expired underneath us — clear and fall through to resume/create.
            connection.ShareCode = null;
            await notifier.DetachAsync();
        }

        // Resume: re-bind an existing session (presenter reconnected within the TTL grace window).
        if (!string.IsNullOrWhiteSpace(resumeCode))
        {
            var code = ShareCodes.Normalize(resumeCode);
            var session = ShareCodes.IsValid(code) ? await store.GetSessionAsync(code) : null;
            if (session is not null && session.PresenterUserId == userId)
            {
                await store.SetStateAsync(code, ShareSessionStates.Live);
                await store.RefreshTtlAsync(code, SessionTtl);
                await notifier.AttachAsync(code);
                connection.ShareCode = code;
                await bus.PublishAsync(ShareKeys.DataChannel(code), ServerFrames.SessionResumed());
                logger.LogInformation("Share session {Code} resumed by user {UserId}", code, userId);
                return new ShareSessionDto(code, await store.GetViewerCountAsync(code));
            }
            // Unknown/expired/foreign resume code — silently start a fresh session instead.
        }

        // Create: claim a fresh cryptographically random code (set-if-absent; retry on collision).
        for (var attempt = 0; attempt < CodeClaimAttempts; attempt++)
        {
            var code = ShareCodes.Generate();
            var session = new ShareSession(code, userId, ShareSessionStates.Live, 0, null, [], DateTime.UtcNow);
            if (!await store.TryClaimCodeAsync(session, SessionTtl))
                continue;

            await notifier.AttachAsync(code);
            connection.ShareCode = code;
            logger.LogInformation("Share session {Code} started by user {UserId}", code, userId);
            return new ShareSessionDto(code, 0);
        }

        // ~900M code combinations — hitting 5 collisions in a row means something is very wrong.
        throw RpcErrors.Conflict("Could not allocate a share code — try again");
    }

    /// <summary>Ends this connection's session and tells all viewers. Idempotent.</summary>
    public async Task StopAsync()
    {
        if (connection.ShareCode is null)
            return;

        var code = connection.ShareCode;
        connection.ShareCode = null;
        await notifier.DetachAsync();
        await store.DeleteSessionAsync(code);
        await bus.PublishAsync(ShareKeys.DataChannel(code), ServerFrames.SessionEnded(SessionEndReasons.Stopped));
        logger.LogInformation("Share session {Code} stopped by its presenter", code);
    }

    /// <summary>
    /// The hot path (~20×/s while drawing): validates the envelope of each message in the batch,
    /// updates the stored <c>seq</c> (and cached snapshot + <c>fileIds</c> when a snapshot is
    /// included), slides the TTL, and relays every message verbatim to the session's data channel.
    /// </summary>
    public async Task PublishAsync(IReadOnlyList<JsonElement>? messages)
    {
        var code = connection.ShareCode
            ?? throw RpcErrors.NotFound("No active share session — call sharing.v1.start first");

        if (messages is null || messages.Count == 0)
            throw RpcErrors.Validation("messages must contain at least one envelope");

        var maxBytes = configuration.GetValue("Sharing:MaxMessageBytes", DefaultMaxMessageBytes);
        long totalBytes = 0;
        var envelopes = new List<ShareEnvelope>(messages.Count);
        foreach (var message in messages)
        {
            var envelope = ShareEnvelopes.Parse(message);
            totalBytes += Encoding.UTF8.GetByteCount(envelope.RawJson);
            envelopes.Add(envelope);
        }

        if (totalBytes > maxBytes)
            throw RpcErrors.PayloadTooLarge($"Publish batch exceeds the maximum size of {maxBytes} bytes");

        // The last snapshot in the batch (if any) becomes the cached late-joiner snapshot.
        ShareEnvelope? snapshot = null;
        for (var i = envelopes.Count - 1; i >= 0; i--)
        {
            if (envelopes[i].Type == ShareEnvelopes.SnapshotType)
            {
                snapshot = envelopes[i];
                break;
            }
        }

        var updated = await store.UpdateSessionAsync(code, envelopes[^1].Seq, snapshot?.RawJson, snapshot?.FileIds, SessionTtl);
        if (!updated)
        {
            await HandleExpiredAsync(code);
            throw RpcErrors.NotFound("Share session expired");
        }

        var channel = ShareKeys.DataChannel(code);
        foreach (var envelope in envelopes)
            await bus.PublishAsync(channel, envelope.RawJson);
    }

    /// <summary>
    /// Slides the session TTL (the presenter app calls this every ~30 s even when idle) and reports
    /// the current viewer count.
    /// </summary>
    public async Task<ShareSessionDto> HeartbeatAsync()
    {
        var code = connection.ShareCode
            ?? throw RpcErrors.NotFound("No active share session — call sharing.v1.start first");

        if (!await store.RefreshTtlAsync(code, SessionTtl))
        {
            await HandleExpiredAsync(code);
            throw RpcErrors.NotFound("Share session expired");
        }

        return new ShareSessionDto(code, await store.GetViewerCountAsync(code));
    }

    /// <summary>
    /// Called when the presenter's WebSocket closes (see WsEndpoints): pauses the session instead of
    /// ending it, so the remaining TTL becomes a reconnect grace window — the presenter can call
    /// <c>sharing.v1.start</c> with a <c>resumeCode</c> on its next connection. Heartbeats stop with
    /// the connection, so the TTL is no longer refreshed and the session expires naturally.
    /// </summary>
    public async Task OnPresenterDisconnectedAsync()
    {
        if (connection.ShareCode is null)
            return;

        var code = connection.ShareCode;
        connection.ShareCode = null;
        await notifier.DetachAsync();

        if (await store.SetStateAsync(code, ShareSessionStates.Paused))
        {
            await bus.PublishAsync(ShareKeys.DataChannel(code), ServerFrames.SessionPaused());
            logger.LogInformation("Share session {Code} paused — presenter disconnected", code);
        }
    }

    /// <summary>The session vanished under a live connection — tell the presenter and unbind.</summary>
    private async Task HandleExpiredAsync(string code)
    {
        connection.ShareCode = null;
        await notifier.NotifyEndedAsync(SessionEndReasons.Expired);
        await notifier.DetachAsync();
        logger.LogInformation("Share session {Code} expired under a connected presenter", code);
    }
}

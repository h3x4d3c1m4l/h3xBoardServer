using H3xBoardServer.Services.Sharing;
using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

/// <summary>
/// JSON-RPC methods for live board sharing — v1. All methods require authentication and operate on
/// this connection's share session (a connection presents at most one session at a time). Viewers
/// do not use these methods — they connect anonymously to <c>/ws/v1/view/{code}</c>.
/// See <c>docs/live-sharing.md</c>.
/// </summary>
public class SharingRpcV1(ShareSessionService sharing)
{
    /// <summary>
    /// Starts a share session and returns its code. Idempotent: returns the connection's existing
    /// session if there is one; resumes the <c>resumeCode</c> session when it exists and belongs to
    /// the caller; otherwise claims a fresh code.
    /// </summary>
    [JsonRpcMethod("sharing.v1.start")]
    public Task<ShareSessionDto> Start(StartSharingRequest? request = null)
    {
        return sharing.StartAsync(request?.ResumeCode);
    }

    /// <summary>Ends the session — viewers receive <c>sessionEnded {reason:"stopped"}</c>.</summary>
    [JsonRpcMethod("sharing.v1.stop")]
    public Task Stop()
    {
        return sharing.StopAsync();
    }

    /// <summary>
    /// Publishes a batch of opaque envelopes to the session's viewers. Called at high frequency
    /// while drawing. A <c>snapshot</c> envelope is additionally cached for late joiners.
    /// </summary>
    [JsonRpcMethod("sharing.v1.publish")]
    public Task Publish(PublishShareRequest request)
    {
        return sharing.PublishAsync(request.Messages);
    }

    /// <summary>Refreshes the session TTL; call every ~30 s while sharing (even when idle).</summary>
    [JsonRpcMethod("sharing.v1.heartbeat")]
    public Task<ShareSessionDto> Heartbeat()
    {
        return sharing.HeartbeatAsync();
    }
}

using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

/// <summary>
/// Per-WebSocket-connection JSON-RPC state. Scoped to the connection lifetime, like
/// <see cref="RpcContext"/>. WsEndpoints sets <see cref="JsonRpc"/> right after constructing it so
/// scoped services can push server→client notifications on the same connection.
/// <see cref="ShareCode"/> tracks this connection's live share session (at most one per connection).
/// </summary>
public class RpcConnection
{
    /// <summary>The connection's JsonRpc instance; null until the WebSocket handler attaches it.</summary>
    public JsonRpc? JsonRpc { get; set; }

    /// <summary>The connection's active share-session code, or null when it is not presenting.</summary>
    public string? ShareCode { get; set; }
}

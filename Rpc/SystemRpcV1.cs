using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

/// <summary>
/// Diagnostic JSON-RPC methods — v1. Registered only in the Development environment
/// (see WsEndpoints). Used to observe how the server surfaces unexpected errors.
/// </summary>
public class SystemRpcV1
{
    /// <summary>
    /// Deliberately throws an unhandled exception so a client can inspect the
    /// JSON-RPC error response for an unexpected server error.
    /// </summary>
    [JsonRpcMethod("system.v1.throw")]
    public void Throw()
        => throw new InvalidOperationException("Deliberate test failure from system.v1.throw");
}

namespace H3xBoardServer.Rpc;

/// <summary>
/// Per-WebSocket-connection authentication state. Scoped to the connection lifetime.
/// Pre-populated from the HTTP session before JSON-RPC starts.
/// </summary>
public class RpcContext
{
    public string? UserId { get; private set; }
    public string? Email { get; private set; }

    public void SetAuthenticated(string userId, string email)
    {
        UserId = userId;
        Email = email;
    }
}

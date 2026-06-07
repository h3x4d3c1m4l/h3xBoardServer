namespace H3xBoardServer.Rpc;

/// <summary>
/// Per-WebSocket-connection authentication state. Scoped to the connection lifetime.
/// Pre-populated from the HTTP session before JSON-RPC starts.
/// </summary>
public class RpcContext
{
    public string? UserId { get; private set; }
    public string? Email { get; private set; }
    public bool IsAuthenticated => UserId is not null;

    public void SetAuthenticated(string userId, string email)
    {
        UserId = userId;
        Email = email;
    }

    public void Clear()
    {
        UserId = null;
        Email = null;
    }

    public void RequireAuthentication()
    {
        if (!IsAuthenticated)
            throw RpcErrors.Unauthenticated();
    }
}

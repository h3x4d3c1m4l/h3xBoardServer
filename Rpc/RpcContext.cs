namespace H3xBoardServer.Rpc;

/// <summary>
/// Per-WebSocket-connection authentication state. Scoped to the connection lifetime.
/// Pre-populated from the HTTP session before JSON-RPC starts.
/// </summary>
public class RpcContext
{
    public int? UserId { get; private set; }
    public string? Email { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;

    public void SetAuthenticated(int userId, string email)
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

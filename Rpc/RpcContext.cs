namespace H3xBoardServer.Rpc;

/// <summary>
/// Per-WebSocket-connection authentication state. Scoped to the connection lifetime.
/// </summary>
public class RpcContext
{
    public int? UserId { get; private set; }
    public string? Username { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;

    public void SetAuthenticated(int userId, string username)
    {
        UserId = userId;
        Username = username;
    }

    public void Clear()
    {
        UserId = null;
        Username = null;
    }

    public void RequireAuthentication()
    {
        if (!IsAuthenticated)
            throw RpcErrors.Unauthenticated();
    }
}

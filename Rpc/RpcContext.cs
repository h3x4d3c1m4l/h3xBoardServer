namespace H3xBoardServer.Rpc;

/// <summary>
/// Per-WebSocket-connection authentication state. Scoped to the connection lifetime.
/// </summary>
public class RpcContext
{
    public int? UserId { get; private set; }
    public string? Email { get; private set; }
    public string? CurrentReconnectToken { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;

    public void SetAuthenticated(int userId, string email, string reconnectToken)
    {
        UserId = userId;
        Email = email;
        CurrentReconnectToken = reconnectToken;
    }

    public void Clear()
    {
        UserId = null;
        Email = null;
        CurrentReconnectToken = null;
    }

    public void RequireAuthentication()
    {
        if (!IsAuthenticated)
            throw RpcErrors.Unauthenticated();
    }
}

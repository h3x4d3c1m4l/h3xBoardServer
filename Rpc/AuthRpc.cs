using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

/// <summary>
/// JSON-RPC methods for authentication — v1.
/// All methods are available on unauthenticated connections so the client can log in first.
/// </summary>
public class AuthRpcV1(AuthService authService, RpcContext context)
{
    [JsonRpcMethod("auth.v1.register")]
    public Task<LoginResult> Register(RegisterRequest request)
        => authService.RegisterAsync(request, context);

    /// <summary>
    /// Logs in and marks the current WebSocket connection as authenticated.
    /// Returns a reconnect token the client should persist for future reconnections.
    /// </summary>
    [JsonRpcMethod("auth.v1.login")]
    public Task<LoginResult> Login(LoginRequest request)
        => authService.LoginAsync(request, context);

    /// <summary>
    /// Exchanges a reconnect token for a new one (rotation — old token is revoked).
    /// Also re-authenticates the current connection.
    /// </summary>
    [JsonRpcMethod("auth.v1.reconnect")]
    public Task<ReconnectResult> Reconnect(ReconnectRequest request)
        => authService.ReconnectAsync(request, context);

    /// <summary>
    /// Revokes the current session's reconnect token. Other sessions are unaffected.
    /// </summary>
    [JsonRpcMethod("auth.v1.logout")]
    public async Task Logout()
    {
        context.RequireAuthentication();
        await authService.LogoutAsync(context.CurrentReconnectToken!);
        context.Clear();
    }

    /// <summary>
    /// Returns the authenticated user's id and email, or throws if not authenticated.
    /// </summary>
    [JsonRpcMethod("auth.v1.whoami")]
    public object Whoami()
    {
        context.RequireAuthentication();
        return new { userId = context.UserId, email = context.Email };
    }
}

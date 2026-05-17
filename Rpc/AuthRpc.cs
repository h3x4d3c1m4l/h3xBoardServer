using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

/// <summary>
/// JSON-RPC methods for authentication.
/// All methods are available on unauthenticated connections so the client can log in first.
/// </summary>
public class AuthRpc(AuthService authService, RpcContext context)
{
    [JsonRpcMethod("auth.register")]
    public Task<LoginResult> Register(RegisterRequest request)
        => authService.RegisterAsync(request);

    /// <summary>
    /// Logs in and marks the current WebSocket connection as authenticated.
    /// Returns JWT tokens the client should persist for future reconnections.
    /// </summary>
    [JsonRpcMethod("auth.login")]
    public Task<LoginResult> Login(LoginRequest request)
        => authService.LoginAsync(request, context);

    /// <summary>
    /// Exchanges a refresh token for a new access token (token rotation — old refresh token is revoked).
    /// </summary>
    [JsonRpcMethod("auth.refreshToken")]
    public Task<TokenResult> RefreshToken(RefreshTokenRequest request)
        => authService.RefreshTokenAsync(request, context);

    /// <summary>
    /// Revokes all refresh tokens for the authenticated user.
    /// </summary>
    [JsonRpcMethod("auth.logout")]
    public async Task Logout()
    {
        context.RequireAuthentication();
        await authService.LogoutAsync(context.UserId!.Value);
        context.Clear();
    }

    /// <summary>
    /// Returns the authenticated user's id and username, or throws if not authenticated.
    /// Useful as a ping/whoami check.
    /// </summary>
    [JsonRpcMethod("auth.whoami")]
    public object Whoami()
    {
        context.RequireAuthentication();
        return new { userId = context.UserId, username = context.Username };
    }
}

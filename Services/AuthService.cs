using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace H3xBoardServer.Services;

public class AuthService(H3xBoardDbFactory dbFactory, IConfiguration config)
{
    private readonly int _reconnectTokenExpiryDays =
        int.TryParse(config["Auth:ReconnectTokenExpiryDays"], out var d) ? d : 30;

    public async Task<LoginResult> RegisterAsync(RegisterRequest request, RpcContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            throw RpcErrors.Validation("Email is required");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw RpcErrors.Validation("Password must be at least 8 characters");

        await using var db = dbFactory.Create();

        var exists = await db.Users
            .Where(u => u.Email == request.Email)
            .Take(1).AsAsyncEnumerable().AnyAsync();

        if (exists)
            throw RpcErrors.Conflict("Email is already registered");

        var now = DateTime.UtcNow;
        var user = new UserEntity
        {
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            CreatedAt = now,
            UpdatedAt = now,
        };

        var userId = await db.InsertWithInt32IdentityAsync(user);
        var reconnectToken = await IssueReconnectTokenAsync(userId);

        context.SetAuthenticated(userId, request.Email, reconnectToken);

        return new LoginResult(reconnectToken, userId, request.Email);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, RpcContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            throw RpcErrors.InvalidCredentials();

        await using var db = dbFactory.Create();

        var user = await db.Users
            .Where(u => u.Email == request.Email)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw RpcErrors.InvalidCredentials();

        var reconnectToken = await IssueReconnectTokenAsync(user.Id);

        context.SetAuthenticated(user.Id, user.Email, reconnectToken);

        return new LoginResult(reconnectToken, user.Id, user.Email);
    }

    public async Task<ReconnectResult> ReconnectAsync(ReconnectRequest request, RpcContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ReconnectToken))
            throw RpcErrors.Validation("Reconnect token is required");

        await using var db = dbFactory.Create();

        var tokenEntity = await db.ReconnectTokens
            .Where(t => t.Token == request.ReconnectToken && !t.IsRevoked)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();

        if (tokenEntity == null || tokenEntity.ExpiresAt <= DateTime.UtcNow)
            throw RpcErrors.Unauthenticated("Reconnect token is invalid or expired");

        var user = await db.Users
            .Where(u => u.Id == tokenEntity.UserId)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("User not found");

        // Revoke the used token (rotation — each token is single-use)
        tokenEntity.IsRevoked = true;
        await db.UpdateAsync(tokenEntity);

        var newToken = await IssueReconnectTokenAsync(user.Id);

        context.SetAuthenticated(user.Id, user.Email, newToken);

        return new ReconnectResult(newToken);
    }

    public async Task LogoutAsync(string reconnectToken)
    {
        await using var db = dbFactory.Create();
        await db.ReconnectTokens
            .Where(t => t.Token == reconnectToken)
            .Set(t => t.IsRevoked, true)
            .UpdateAsync();
    }

    private async Task<string> IssueReconnectTokenAsync(int userId)
    {
        var token = GenerateToken();
        var now = DateTime.UtcNow;

        await using var db = dbFactory.Create();
        await db.InsertAsync(new ReconnectTokenEntity
        {
            UserId = userId,
            Token = token,
            ExpiresAt = now.AddDays(_reconnectTokenExpiryDays),
            CreatedAt = now,
            IsRevoked = false,
        });

        return token;
    }

    private static string GenerateToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}

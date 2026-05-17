using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace H3xBoardServer.Services;

public class AuthService(H3xBoardDbFactory dbFactory, IConfiguration config)
{
    private readonly string _secretKey = config["Jwt:SecretKey"]
        ?? throw new InvalidOperationException("Jwt:SecretKey is not configured");
    private readonly string _issuer = config["Jwt:Issuer"] ?? "h3xboard-server";
    private readonly string _audience = config["Jwt:Audience"] ?? "h3xboard-client";
    private readonly int _accessTokenExpiryMinutes =
        int.TryParse(config["Jwt:AccessTokenExpiryMinutes"], out var m) ? m : 60;
    private readonly int _refreshTokenExpiryDays =
        int.TryParse(config["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 30;

    public async Task<LoginResult> RegisterAsync(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            throw RpcErrors.Validation("Username is required");
        if (string.IsNullOrWhiteSpace(request.Email))
            throw RpcErrors.Validation("Email is required");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw RpcErrors.Validation("Password must be at least 8 characters");

        await using var db = dbFactory.Create();

        var existingUser = await db.Users
            .Where(u => u.Username == request.Username || u.Email == request.Email)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();

        if (existingUser != null)
        {
            var field = existingUser.Username == request.Username ? "Username" : "Email";
            throw RpcErrors.Conflict($"{field} is already taken");
        }

        var now = DateTime.UtcNow;
        var user = new UserEntity
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            CreatedAt = now,
            UpdatedAt = now,
        };

        var userId = await db.InsertWithInt32IdentityAsync(user);

        return await IssueTokensAsync(userId, request.Username);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, RpcContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            throw RpcErrors.InvalidCredentials();

        await using var db = dbFactory.Create();

        var user = await db.Users
            .Where(u => u.Username == request.Username)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw RpcErrors.InvalidCredentials();

        context.SetAuthenticated(user.Id, user.Username);

        return await IssueTokensAsync(user.Id, user.Username);
    }

    public async Task<TokenResult> RefreshTokenAsync(RefreshTokenRequest request, RpcContext context)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw RpcErrors.Validation("Refresh token is required");

        await using var db = dbFactory.Create();

        var tokenEntity = await db.RefreshTokens
            .Where(t => t.Token == request.RefreshToken && !t.IsRevoked)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();

        if (tokenEntity == null || tokenEntity.ExpiresAt <= DateTime.UtcNow)
            throw RpcErrors.Unauthenticated("Refresh token is invalid or expired");

        var user = await db.Users.Where(u => u.Id == tokenEntity.UserId).Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("User not found");

        // Revoke the used refresh token (rotation)
        tokenEntity.IsRevoked = true;
        await db.UpdateAsync(tokenEntity);

        // Issue new tokens and update context
        context.SetAuthenticated(user.Id, user.Username);
        var result = await IssueTokensAsync(user.Id, user.Username);

        return new TokenResult(result.AccessToken, result.AccessTokenExpiresInSeconds);
    }

    public async Task LogoutAsync(int userId)
    {
        await using var db = dbFactory.Create();
        await db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .Set(t => t.IsRevoked, true)
            .UpdateAsync();
    }

    /// <summary>
    /// Validates a JWT and sets authentication state on the context.
    /// Called when a WebSocket connection provides a token in the query string.
    /// Throws if the token is invalid or expired.
    /// </summary>
    public async Task AuthenticateFromTokenAsync(string token, RpcContext context)
    {
        var (userId, username) = ValidateAccessToken(token);

        // Confirm user still exists
        await using var db = dbFactory.Create();
        var exists = await db.Users.Where(u => u.Id == userId).AsAsyncEnumerable().AnyAsync();
        if (!exists)
            throw RpcErrors.Unauthenticated("User no longer exists");

        context.SetAuthenticated(userId, username);
    }

    private async Task<LoginResult> IssueTokensAsync(int userId, string username)
    {
        var accessToken = GenerateAccessToken(userId, username);
        var refreshToken = GenerateRefreshToken();
        var now = DateTime.UtcNow;

        await using var db = dbFactory.Create();
        await db.InsertAsync(new RefreshTokenEntity
        {
            UserId = userId,
            Token = refreshToken,
            ExpiresAt = now.AddDays(_refreshTokenExpiryDays),
            CreatedAt = now,
            IsRevoked = false,
        });

        return new LoginResult(
            accessToken,
            refreshToken,
            _accessTokenExpiryMinutes * 60,
            userId,
            username);
    }

    private string GenerateAccessToken(int userId, string username)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private (int userId, string username) ValidateAccessToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        var handler = new JwtSecurityTokenHandler();
        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(token, parameters, out _);
        }
        catch (Exception ex)
        {
            throw RpcErrors.Unauthenticated($"Invalid token: {ex.Message}");
        }

        var userIdClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw RpcErrors.Unauthenticated("Token missing sub claim");
        var username = principal.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value
            ?? throw RpcErrors.Unauthenticated("Token missing unique_name claim");

        return (int.Parse(userIdClaim), username);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}

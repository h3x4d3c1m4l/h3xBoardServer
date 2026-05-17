namespace H3xBoardServer.Rpc.Dtos;

public record RegisterRequest(string Username, string Email, string Password);

public record LoginRequest(string Username, string Password);

public record LoginResult(
    string AccessToken,
    string RefreshToken,
    int AccessTokenExpiresInSeconds,
    int UserId,
    string Username);

public record RefreshTokenRequest(string RefreshToken);

public record TokenResult(
    string AccessToken,
    int AccessTokenExpiresInSeconds);

namespace H3xBoardServer.Rpc.Dtos;

public record RegisterRequest(string Email, string Password);

public record LoginRequest(string Email, string Password);

public record LoginResult(
    string ReconnectToken,
    int UserId,
    string Email);

public record ReconnectRequest(string ReconnectToken);

public record ReconnectResult(string ReconnectToken);

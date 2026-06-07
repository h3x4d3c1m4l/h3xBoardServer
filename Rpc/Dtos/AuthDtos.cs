namespace H3xBoardServer.Rpc.Dtos;

public record RegisterRequest(string Email, string Password);

public record LoginRequest(string Email, string Password);

public record AuthResult(string UserId, string Email);

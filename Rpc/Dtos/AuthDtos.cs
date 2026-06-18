namespace H3xBoardServer.Rpc.Dtos;

public record RegisterRequest(string Email, string Password, string? FirstName = null, string? LastName = null);

public record LoginRequest(string Email, string Password);

public record AuthResult(string UserId, string Email, string? FirstName = null, string? LastName = null);

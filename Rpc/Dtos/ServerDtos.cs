namespace H3xBoardServer.Rpc.Dtos;

/// <summary>
/// Basic, unauthenticated server information. Intended to grow over time. Advertises whether new
/// registrations are accepted and the maximum file upload size so clients can validate before sending.
/// </summary>
public record ServerInfo(bool RegistrationAllowed, long MaxUploadBytes);

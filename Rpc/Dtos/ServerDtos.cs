namespace H3xBoardServer.Rpc.Dtos;

/// <summary>
/// Basic, unauthenticated server information. Intended to grow over time;
/// for now it only advertises whether new registrations are accepted.
/// </summary>
public record ServerInfo(bool RegistrationAllowed);

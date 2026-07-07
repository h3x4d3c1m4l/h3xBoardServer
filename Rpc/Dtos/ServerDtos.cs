namespace H3xBoardServer.Rpc.Dtos;

/// <summary>
/// Basic, unauthenticated server information. Intended to grow over time. Advertises whether new
/// registrations are accepted and the maximum file upload size so clients can validate before sending.
/// <paramref name="Warning"/> is an optional server-wide banner message (null when unset) — e.g. to
/// flag a testing-only environment where data loss may occur — that clients should surface in their UI.
/// </summary>
public record ServerInfo(bool RegistrationAllowed, long MaxUploadBytes, string? Warning);

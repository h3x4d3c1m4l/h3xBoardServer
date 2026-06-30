namespace H3xBoardServer.Rpc.Dtos;

/// <summary>
/// A single user setting. <c>Value</c> is the opaque JSON value (bool, string, object, …) owned by
/// the client, except for keys the server knows about (see <c>Settings/KnownSettings.cs</c>).
/// </summary>
public record SettingDto(
    string Key,
    JsonElement Value,
    DateTime UpdatedAt);

/// <summary>Upsert one setting key to <c>Value</c> (per-key patch; other keys are untouched).</summary>
public record SetSettingRequest(
    string Key,
    JsonElement Value);

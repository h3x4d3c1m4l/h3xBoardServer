using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

/// <summary>
/// JSON-RPC methods for per-user settings — v1. Per-key patch semantics: each call touches a single
/// key, so concurrent edits to different keys never clobber each other. All methods require
/// authentication and operate on the authenticated user's settings.
/// </summary>
public class SettingsRpcV1(SettingsService settingsService, RpcContext context)
{
    /// <summary>Lists all stored settings for the user (does not synthesize known-key defaults).</summary>
    [JsonRpcMethod("settings.v1.getAll")]
    public Task<List<SettingDto>> GetAll()
    {
        return settingsService.GetAllAsync(context.UserId!);
    }

    /// <summary>
    /// Returns one setting. Falls back to the registry default for a known-but-unset key; throws
    /// not-found for an unknown unset key.
    /// </summary>
    [JsonRpcMethod("settings.v1.get")]
    public Task<SettingDto> Get(string key)
    {
        return settingsService.GetAsync(context.UserId!, key);
    }

    /// <summary>Upserts one setting key to a new value.</summary>
    [JsonRpcMethod("settings.v1.set")]
    public Task<SettingDto> Set(SetSettingRequest request)
    {
        return settingsService.SetAsync(context.UserId!, request.Key, request.Value);
    }

    /// <summary>Removes one setting key (reverting known keys to their server default).</summary>
    [JsonRpcMethod("settings.v1.delete")]
    public Task Delete(string key)
    {
        return settingsService.DeleteAsync(context.UserId!, key);
    }
}

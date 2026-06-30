using System.Text;
using System.Text.RegularExpressions;
using H3xBoardServer.Settings;

namespace H3xBoardServer.Services;

/// <summary>
/// Per-user settings business logic. Settings live one row per <c>(user_id, key)</c> in the
/// <c>user_settings</c> table; every query is owner-scoped so settings never leak across accounts.
/// Values are stored as raw JSON text and round-trip as <see cref="JsonElement"/> (same trick as
/// <c>boards.data</c>). Keys the server reads have an entry in <see cref="KnownSettings"/> that
/// supplies a default and a type check; unknown keys are stored verbatim as a client-owned bag.
/// </summary>
public partial class SettingsService(H3xBoardDbFactory dbFactory, IConfiguration configuration)
{
    private const int MaxKeyLength = 128;
    private const long DefaultMaxValueBytes = 16L * 1024;
    private const int DefaultMaxKeysPerUser = 256;

    // Conservative key charset — dotted/segmented identifiers like "ui.theme" or "editor.grid-size".
    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex KeyPattern();

    public async Task<List<SettingDto>> GetAllAsync(string userId)
    {
        await using var db = dbFactory.Create();
        var rows = await db.UserSettings
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Key)
            .ToListAsync();
        return rows.Select(MapToDto).ToList();
    }

    /// <summary>
    /// Returns the stored value for <paramref name="key"/>, or the registry default if the key is
    /// known but unset, else throws <see cref="RpcErrors.NotFound"/>.
    /// </summary>
    public async Task<SettingDto> GetAsync(string userId, string key)
    {
        ValidateKey(key);

        await using var db = dbFactory.Create();
        var row = await db.UserSettings
            .Where(s => s.UserId == userId && s.Key == key)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();

        if (row is not null)
            return MapToDto(row);

        var known = KnownSettings.Find(key)
            ?? throw RpcErrors.NotFound("Setting not found");
        // Known but never set — serve the default (UpdatedAt is the epoch, signalling "never written").
        return new SettingDto(key, known.DefaultValue, default);
    }

    /// <summary>Upserts one key. Validates the key, and (for known keys) the value's type. Per-key — no other key is touched.</summary>
    public async Task<SettingDto> SetAsync(string userId, string key, JsonElement value)
    {
        ValidateKey(key);

        var raw = value.GetRawText();
        var maxBytes = configuration.GetValue("Settings:MaxValueBytes", DefaultMaxValueBytes);
        if (Encoding.UTF8.GetByteCount(raw) > maxBytes)
            throw RpcErrors.Validation($"Setting value exceeds the maximum size of {maxBytes} bytes");

        if (KnownSettings.Find(key) is { } known && !known.Accepts(value))
            throw RpcErrors.Validation($"Value is not valid for setting '{key}'");

        var now = DateTime.UtcNow;

        await using var db = dbFactory.Create();
        var existing = await db.UserSettings
            .Where(s => s.UserId == userId && s.Key == key)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();

        if (existing is not null)
        {
            existing.Value = raw;
            existing.UpdatedAt = now;
            await db.UpdateAsync(existing);
            return MapToDto(existing);
        }

        // New key — guard against unbounded growth of the client-owned bag.
        var maxKeys = configuration.GetValue("Settings:MaxKeysPerUser", DefaultMaxKeysPerUser);
        var count = await db.UserSettings.CountAsync(s => s.UserId == userId);
        if (count >= maxKeys)
            throw RpcErrors.Validation($"Too many settings (maximum {maxKeys} per user)");

        var entity = new UserSettingEntity
        {
            UserId = userId,
            Key = key,
            Value = raw,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await db.InsertAsync(entity);
        return MapToDto(entity);
    }

    /// <summary>Removes a key. A no-op if absent; deleting a known key reverts the server to its default.</summary>
    public async Task DeleteAsync(string userId, string key)
    {
        ValidateKey(key);
        await using var db = dbFactory.Create();
        await db.UserSettings.Where(s => s.UserId == userId && s.Key == key).DeleteAsync();
    }

    /// <summary>
    /// Server-internal typed accessor: deserializes the stored value (or the <see cref="KnownSettings"/>
    /// default when unset) to <typeparamref name="T"/>, so other services can read a setting without
    /// touching JSON. Returns <c>default</c> when the key is neither stored nor known.
    /// </summary>
    public async Task<T?> GetValueAsync<T>(string userId, string key)
    {
        await using var db = dbFactory.Create();
        var row = await db.UserSettings
            .Where(s => s.UserId == userId && s.Key == key)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();

        var json = row?.Value ?? KnownSettings.Find(key)?.DefaultJson;
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw RpcErrors.Validation("Setting key is required");
        if (key.Length > MaxKeyLength)
            throw RpcErrors.Validation($"Setting key exceeds {MaxKeyLength} characters");
        if (!KeyPattern().IsMatch(key))
            throw RpcErrors.Validation("Setting key may only contain letters, digits, '.', '_' and '-'");
    }

    private static SettingDto MapToDto(UserSettingEntity entity)
    {
        // Clone so the JsonElement owns its memory independent of the parsed document.
        var value = JsonDocument.Parse(entity.Value).RootElement.Clone();
        return new SettingDto(entity.Key, value, entity.UpdatedAt);
    }
}

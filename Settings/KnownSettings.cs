namespace H3xBoardServer.Settings;

/// <summary>The value shape a known setting must have. Maps to one or more <see cref="JsonValueKind"/>.</summary>
public enum SettingType
{
    Bool,
    String,
    Number,
    Object,
    Array,
}

/// <summary>
/// Describes a setting key the <b>server</b> understands: its value type, default, and optional extra
/// validation. This is the single source of truth for server-readable preferences — defaults are
/// applied on read (<c>settings.v1.get</c> / <see cref="H3xBoardServer.Services.SettingsService.GetValueAsync{T}"/>)
/// and the type is enforced on write. Keys absent from <see cref="KnownSettings.All"/> are still
/// allowed, but treated as an opaque client-owned bag (no default, no type check).
/// </summary>
/// <param name="Key">Wire key, e.g. <c>"ui.theme"</c>.</param>
/// <param name="Type">Required value shape.</param>
/// <param name="DefaultJson">Default value as raw JSON, returned when no row exists.</param>
/// <param name="Validate">Optional extra check run after the type matches (e.g. an allowed-value set).</param>
public record KnownSetting(string Key, SettingType Type, string DefaultJson, Func<JsonElement, bool>? Validate = null)
{
    /// <summary>The default value as an independent <see cref="JsonElement"/> (owns its own memory).</summary>
    public JsonElement DefaultValue => JsonDocument.Parse(DefaultJson).RootElement.Clone();

    /// <summary>True if <paramref name="value"/> has the right JSON shape and passes <see cref="Validate"/>.</summary>
    public bool Accepts(JsonElement value)
    {
        var kindOk = Type switch
        {
            SettingType.Bool => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SettingType.String => value.ValueKind is JsonValueKind.String,
            SettingType.Number => value.ValueKind is JsonValueKind.Number,
            SettingType.Object => value.ValueKind is JsonValueKind.Object,
            SettingType.Array => value.ValueKind is JsonValueKind.Array,
            _ => false,
        };
        return kindOk && (Validate is null || Validate(value));
    }
}

/// <summary>
/// Registry of server-readable settings. Add an entry here when the server needs to act on a key
/// (defaults, validation, feature gating); purely client-owned preferences need no entry. The list
/// below is a starting point — extend it as the app grows.
/// </summary>
public static class KnownSettings
{
    public static readonly IReadOnlyDictionary<string, KnownSetting> All =
        new[]
        {
            // Whether the server may send this user e-mail notifications.
            new KnownSetting("notifications.email", SettingType.Bool, "true"),

            // UI theme preference. The server doesn't render UI, but keeping it typed lets the
            // default ("system") be served centrally and guards against bad values.
            new KnownSetting("ui.theme", SettingType.String, "\"system\"",
                v => v.GetString() is "system" or "light" or "dark"),
        }
        .ToDictionary(s => s.Key);

    public static KnownSetting? Find(string key) => All.GetValueOrDefault(key);
}

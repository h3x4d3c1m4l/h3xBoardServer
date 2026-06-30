namespace H3xBoardServer.Data.Entities;

/// <summary>
/// A single per-user preference: one row per <c>(user_id, key)</c>. <see cref="Value"/> is raw JSON
/// text (a bool, number, string, object, …) so any value shape round-trips, exactly like
/// <see cref="BoardEntity.Data"/>. Keys the server itself reads are described in
/// <see cref="H3xBoardServer.Settings.KnownSettings"/>; unknown keys are stored verbatim as a
/// client-owned bag.
/// </summary>
[Table("user_settings")]
public class UserSettingEntity
{
    [PrimaryKey(0)]
    [Column("user_id")]
    public string UserId { get; set; } = null!;

    [PrimaryKey(1)]
    [Column("key")]
    public string Key { get; set; } = null!;

    /// <summary>The setting value as raw JSON text. Opaque to the DB layer.</summary>
    [Column("value"), NotNull]
    public string Value { get; set; } = null!;

    [Column("created_at"), NotNull]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at"), NotNull]
    public DateTime UpdatedAt { get; set; }
}

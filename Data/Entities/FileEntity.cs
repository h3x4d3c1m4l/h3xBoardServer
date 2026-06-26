namespace H3xBoardServer.Data.Entities;

[Table("files")]
public class FileEntity
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// Owner scope — currently always <c>"user"</c>. Exists so a future <c>"company"</c> scope
    /// can be added without a schema change. Together with <see cref="OwnerId"/> it identifies
    /// the owner and drives the storage key prefix (<c>users/{ownerId}/…</c>).
    /// </summary>
    [Column("owner_scope"), NotNull]
    public string OwnerScope { get; set; } = null!;

    [Column("owner_id"), NotNull]
    public string OwnerId { get; set; } = null!;

    /// <summary>
    /// The opaque key the bytes live under in the storage backend (e.g. <c>users/{id}/{fileId}</c>).
    /// </summary>
    [Column("storage_key"), NotNull]
    public string StorageKey { get; set; } = null!;

    /// <summary>
    /// Virtual folder the file lives in (forward-slash separated, <c>""</c> = root). Metadata only —
    /// it is decoupled from <see cref="StorageKey"/>, so moving/renaming never touches the bytes.
    /// </summary>
    [Column("path"), NotNull]
    public string Path { get; set; } = null!;

    /// <summary>Leaf name within <see cref="Path"/>, e.g. <c>"sunset.jpg"</c>. Never a storage path.</summary>
    [Column("file_name"), NotNull]
    public string FileName { get; set; } = null!;

    [Column("content_type"), NotNull]
    public string ContentType { get; set; } = null!;

    [Column("size_bytes"), NotNull]
    public long SizeBytes { get; set; }

    [Column("created_at"), NotNull]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at"), NotNull]
    public DateTime UpdatedAt { get; set; }
}

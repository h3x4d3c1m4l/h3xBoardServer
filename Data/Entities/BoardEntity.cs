namespace H3xBoardServer.Data.Entities;

[Table("boards")]
public class BoardEntity
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Column("user_id"), NotNull]
    public string UserId { get; set; } = null!;

    [Column("title"), NotNull]
    public string Title { get; set; } = null!;

    /// <summary>
    /// Full board state as JSON — opaque to the server, owned by the Flutter client.
    /// Contains board settings (background, lines), widgets, drawing strokes, and tool state.
    /// </summary>
    [Column("data"), NotNull]
    public string Data { get; set; } = "{}";

    /// <summary>
    /// Id of this board's screenshot in the <c>files</c> table (a <see cref="FileKind.BoardScreenshot"/>
    /// file), or null if none has been uploaded yet. The bytes are managed out-of-band via
    /// <c>/api/v1/boards/{id}/screenshot</c>; setting one does not touch <see cref="UpdatedAt"/>.
    /// </summary>
    [Column("screenshot_file_id")]
    public string? ScreenshotFileId { get; set; }

    [Column("created_at"), NotNull]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at"), NotNull]
    public DateTime UpdatedAt { get; set; }
}

namespace H3xBoardServer.Data.Entities;

[Table("boards")]
public class BoardEntity
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Column("user_id"), NotNull]
    public int UserId { get; set; }

    [Column("title"), NotNull]
    public string Title { get; set; } = null!;

    /// <summary>
    /// Full board state as JSON — opaque to the server, owned by the Flutter client.
    /// Contains board settings (background, lines), widgets, drawing strokes, and tool state.
    /// </summary>
    [Column("data"), NotNull]
    public string Data { get; set; } = "{}";

    [Column("created_at"), NotNull]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at"), NotNull]
    public DateTime UpdatedAt { get; set; }
}

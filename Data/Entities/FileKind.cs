namespace H3xBoardServer.Data.Entities;

/// <summary>
/// Classifies a stored file. <see cref="User"/> files are the ones the owner uploaded and browses;
/// every other kind is system-managed and hidden from the generic file API (browse + delete) so it
/// never shows up among the user's own uploads.
/// Persisted as its <see cref="MapValueAttribute"/> string in the <c>files.kind</c> column so the
/// values stay readable and stable (and decoupled from the enum's ordinal).
/// </summary>
public enum FileKind
{
    /// <summary>An ordinary file the owner uploaded — visible in browse, deletable by the owner.</summary>
    [MapValue("user")] User,

    /// <summary>A board screenshot, managed via <c>/api/v1/boards/{id}/screenshot</c>; hidden from browse.</summary>
    [MapValue("board-screenshot")] BoardScreenshot,
}

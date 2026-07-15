namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// A live-sharing session as stored in <see cref="IShareStore"/>. <see cref="SnapshotJson"/> is the
/// presenter's most recent <c>snapshot</c> envelope, stored verbatim — the server never parses board
/// content, it only extracts the <c>fileIds</c> the snapshot references so the anonymous view file
/// endpoint can authorize downloads.
/// </summary>
public record ShareSession(
    string Code,
    string PresenterUserId,
    string State,
    long Seq,
    string? SnapshotJson,
    IReadOnlyList<string> FileIds,
    DateTime CreatedAt);

/// <summary>Values of <see cref="ShareSession.State"/>.</summary>
public static class ShareSessionStates
{
    /// <summary>The presenter is connected and publishing.</summary>
    public const string Live = "live";

    /// <summary>The presenter disconnected; the session TTL is the reconnect grace window.</summary>
    public const string Paused = "paused";
}

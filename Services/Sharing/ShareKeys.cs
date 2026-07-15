namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Redis key / pub-sub channel naming for live sharing. The in-memory fallbacks use the same names
/// so the two implementations stay interchangeable. See <c>docs/live-sharing.md</c>.
/// </summary>
public static class ShareKeys
{
    /// <summary>A viewer counts as present when its zset score is at most this many seconds old.</summary>
    public const int PresenceWindowSeconds = 45;

    /// <summary>Session hash: presenterUserId, state, seq, snapshot, fileIds, createdAt. Sliding TTL.</summary>
    public static string Session(string code) => $"h3xboard:share:session:{code}";

    /// <summary>Viewer presence zset: member = viewer connection guid, score = unix seconds of last ping.</summary>
    public static string Viewers(string code) => $"h3xboard:share:viewers:{code}";

    /// <summary>Data channel: presenter envelopes + server-origin frames, relayed verbatim to viewers.</summary>
    public static string DataChannel(string code) => $"h3xboard:share:channel:{code}";

    /// <summary>Control channel: internal viewer-triggered events for the instance holding the presenter.</summary>
    public static string ControlChannel(string code) => $"h3xboard:share:control:{code}";
}

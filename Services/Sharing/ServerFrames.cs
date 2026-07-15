namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Server-origin frames sent to viewers (over the viewer WebSocket and the per-code data channel).
/// They share the presenter envelope shape but carry <c>"seq":0,"origin":"server"</c> so clients can
/// tell them apart from relayed board content. Built by string interpolation — every value comes
/// from a server-controlled, closed set — with a fixed field order so <see cref="IsSessionEnded"/>
/// can cheaply recognize our own frames without parsing relayed board content.
/// </summary>
public static class ServerFrames
{
    private const string Prefix = "{\"v\":1,\"seq\":0,\"origin\":\"server\"";

    public static string Hello(string state) => $"{Prefix},\"type\":\"hello\",\"state\":\"{state}\"}}";

    public static string SessionPaused() => $"{Prefix},\"type\":\"sessionPaused\"}}";

    public static string SessionResumed() => $"{Prefix},\"type\":\"sessionResumed\"}}";

    public static string SessionEnded(string reason) => $"{Prefix},\"type\":\"sessionEnded\",\"reason\":\"{reason}\"}}";

    public static string ViewerCount(int count) => $"{Prefix},\"type\":\"viewerCount\",\"count\":{count}}}";

    /// <summary>
    /// True when <paramref name="frame"/> is a server-origin <c>sessionEnded</c> frame produced by
    /// <see cref="SessionEnded"/>. A prefix check keeps the hot relay path free of JSON parsing;
    /// only the presenter of the session itself could forge the prefix, which is harmless.
    /// </summary>
    public static bool IsSessionEnded(string frame)
        => frame.StartsWith(Prefix, StringComparison.Ordinal)
            && frame.Contains("\"type\":\"sessionEnded\"", StringComparison.Ordinal);
}

/// <summary>Values of the <c>hello</c> frame's <c>state</c> field.</summary>
public static class HelloStates
{
    /// <summary>The session is live and a snapshot follows immediately after the hello.</summary>
    public const string Live = "live";

    /// <summary>The session exists but is paused or has no snapshot yet — content will follow.</summary>
    public const string Waiting = "waiting";

    /// <summary>No session with that code; the socket closes right after the hello.</summary>
    public const string NotFound = "notFound";

    /// <summary>The session is at Sharing:MaxViewersPerSession; the socket closes right after the hello.</summary>
    public const string Full = "full";
}

/// <summary>Values of the <c>sessionEnded</c> frame's <c>reason</c> field.</summary>
public static class SessionEndReasons
{
    public const string Stopped = "stopped";
    public const string Expired = "expired";
}

/// <summary>
/// Internal frames on the per-code control channel — viewer-triggered events for whichever instance
/// holds the presenter connection. Never sent to app clients; the <c>__</c> prefix keeps them
/// unmistakably internal.
/// </summary>
public static class ControlFrames
{
    public const string SnapshotRequestedType = "__snapshotRequested";
    public const string ViewerCountType = "__viewerCount";

    public static string SnapshotRequested() => "{\"origin\":\"server\",\"type\":\"__snapshotRequested\"}";

    public static string ViewerCount(int count) => $"{{\"origin\":\"server\",\"type\":\"__viewerCount\",\"count\":{count}}}";
}

namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Storage for live-sharing session state and viewer presence. Redis-backed in production
/// (multi-instance safe, restart safe within the TTL) with an in-process fallback for development.
/// All methods take normalized codes — callers normalize via <see cref="ShareCodes.Normalize"/>.
/// </summary>
public interface IShareStore
{
    /// <summary>
    /// Atomically claims <see cref="ShareSession.Code"/> and stores the session (set-if-absent).
    /// Returns false when the code is already taken — the caller generates a new one and retries.
    /// </summary>
    Task<bool> TryClaimCodeAsync(ShareSession session, TimeSpan ttl);

    Task<ShareSession?> GetSessionAsync(string code);

    Task<bool> SessionExistsAsync(string code);

    /// <summary>Slides the session TTL. Returns false when the session no longer exists.</summary>
    Task<bool> RefreshTtlAsync(string code, TimeSpan ttl);

    /// <summary>
    /// Sets the session state (live/paused) without touching the TTL — pausing on presenter
    /// disconnect must not extend the reconnect grace window. Returns false when the session is gone.
    /// </summary>
    Task<bool> SetStateAsync(string code, string state);

    /// <summary>
    /// The publish hot path: updates <c>seq</c>, optionally replaces the cached snapshot + its
    /// <c>fileIds</c>, and slides the TTL — in one round trip. Returns false when the session is gone.
    /// </summary>
    Task<bool> UpdateSessionAsync(string code, long seq, string? snapshotJson, IReadOnlyList<string>? fileIds, TimeSpan ttl);

    /// <summary>Deletes the session and its viewer presence. Idempotent.</summary>
    Task DeleteSessionAsync(string code);

    /// <summary>Adds a viewer or refreshes its presence timestamp (viewers ping periodically).</summary>
    Task AddViewerAsync(string code, string viewerId, TimeSpan ttl);

    Task RemoveViewerAsync(string code, string viewerId);

    /// <summary>
    /// Counts viewers seen within the last <see cref="ShareKeys.PresenceWindowSeconds"/>, pruning
    /// stale entries (viewers whose socket died without a clean remove) as a side effect.
    /// </summary>
    Task<int> GetViewerCountAsync(string code);
}

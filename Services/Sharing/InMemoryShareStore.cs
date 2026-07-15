namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// In-process <see cref="IShareStore"/> fallback for when Redis is not configured. Single-instance
/// only — sessions are lost on restart and invisible to other instances (a startup warning is
/// logged, mirroring the session-cache fallback). Takes a <see cref="TimeProvider"/> so tests can
/// simulate TTL expiry.
/// </summary>
public class InMemoryShareStore(TimeProvider? timeProvider = null) : IShareStore
{
    private sealed class Entry
    {
        public required ShareSession Session { get; set; }
        public required DateTimeOffset ExpiresAt { get; set; }
        public Dictionary<string, DateTimeOffset> Viewers { get; } = [];
    }

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly Lock _lock = new();

    public Task<bool> TryClaimCodeAsync(ShareSession session, TimeSpan ttl)
    {
        lock (_lock)
        {
            if (GetLiveEntry(session.Code) is not null)
                return Task.FromResult(false);
            _entries[session.Code] = new Entry { Session = session, ExpiresAt = _time.GetUtcNow() + ttl };
            return Task.FromResult(true);
        }
    }

    public Task<ShareSession?> GetSessionAsync(string code)
    {
        lock (_lock)
            return Task.FromResult(GetLiveEntry(code)?.Session);
    }

    public Task<bool> SessionExistsAsync(string code)
    {
        lock (_lock)
            return Task.FromResult(GetLiveEntry(code) is not null);
    }

    public Task<bool> RefreshTtlAsync(string code, TimeSpan ttl)
    {
        lock (_lock)
        {
            var entry = GetLiveEntry(code);
            if (entry is null)
                return Task.FromResult(false);
            entry.ExpiresAt = _time.GetUtcNow() + ttl;
            return Task.FromResult(true);
        }
    }

    public Task<bool> SetStateAsync(string code, string state)
    {
        lock (_lock)
        {
            var entry = GetLiveEntry(code);
            if (entry is null)
                return Task.FromResult(false);
            entry.Session = entry.Session with { State = state };
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdateSessionAsync(string code, long seq, string? snapshotJson, IReadOnlyList<string>? fileIds, TimeSpan ttl)
    {
        lock (_lock)
        {
            var entry = GetLiveEntry(code);
            if (entry is null)
                return Task.FromResult(false);
            entry.Session = snapshotJson is null
                ? entry.Session with { Seq = seq }
                : entry.Session with { Seq = seq, SnapshotJson = snapshotJson, FileIds = fileIds ?? [] };
            entry.ExpiresAt = _time.GetUtcNow() + ttl;
            return Task.FromResult(true);
        }
    }

    public Task DeleteSessionAsync(string code)
    {
        lock (_lock)
            _entries.Remove(code);
        return Task.CompletedTask;
    }

    public Task AddViewerAsync(string code, string viewerId, TimeSpan ttl)
    {
        lock (_lock)
        {
            // Presence outlives the session hash in Redis too (separate keys) — mirror that by
            // tracking viewers even when the session has just expired; the watchdog cleans up.
            if (GetLiveEntry(code) is { } entry)
                entry.Viewers[viewerId] = _time.GetUtcNow();
        }
        return Task.CompletedTask;
    }

    public Task RemoveViewerAsync(string code, string viewerId)
    {
        lock (_lock)
        {
            if (GetLiveEntry(code) is { } entry)
                entry.Viewers.Remove(viewerId);
        }
        return Task.CompletedTask;
    }

    public Task<int> GetViewerCountAsync(string code)
    {
        lock (_lock)
        {
            var entry = GetLiveEntry(code);
            if (entry is null)
                return Task.FromResult(0);

            var staleBefore = _time.GetUtcNow() - TimeSpan.FromSeconds(ShareKeys.PresenceWindowSeconds);
            foreach (var stale in entry.Viewers.Where(v => v.Value < staleBefore).Select(v => v.Key).ToList())
                entry.Viewers.Remove(stale);
            return Task.FromResult(entry.Viewers.Count);
        }
    }

    /// <summary>Returns the entry for <paramref name="code"/>, lazily evicting it when expired.</summary>
    private Entry? GetLiveEntry(string code)
    {
        if (!_entries.TryGetValue(code, out var entry))
            return null;
        if (entry.ExpiresAt > _time.GetUtcNow())
            return entry;
        _entries.Remove(code);
        return null;
    }
}

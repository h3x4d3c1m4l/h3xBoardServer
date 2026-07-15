using StackExchange.Redis;

namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Redis-backed <see cref="IShareStore"/>. Sessions live in a hash under
/// <see cref="ShareKeys.Session"/> with a sliding TTL; viewer presence lives in a zset under
/// <see cref="ShareKeys.Viewers"/> scored by unix seconds of the viewer's last ping. Conditional
/// writes go through transactions so a session that expires mid-operation is never resurrected
/// as a TTL-less key.
/// </summary>
public class RedisShareStore(IConnectionMultiplexer redis) : IShareStore
{
    private const string FieldPresenterUserId = "presenterUserId";
    private const string FieldState = "state";
    private const string FieldSeq = "seq";
    private const string FieldSnapshot = "snapshot";
    private const string FieldFileIds = "fileIds";
    private const string FieldCreatedAt = "createdAt";

    private IDatabase Db => redis.GetDatabase();

    public async Task<bool> TryClaimCodeAsync(ShareSession session, TimeSpan ttl)
    {
        var key = ShareKeys.Session(session.Code);
        var transaction = Db.CreateTransaction();
        transaction.AddCondition(Condition.KeyNotExists(key));
        _ = transaction.HashSetAsync(key, ToHashEntries(session));
        _ = transaction.KeyExpireAsync(key, ttl);
        return await transaction.ExecuteAsync();
    }

    public async Task<ShareSession?> GetSessionAsync(string code)
    {
        var entries = await Db.HashGetAllAsync(ShareKeys.Session(code));
        if (entries.Length == 0)
            return null;

        var hash = entries.ToDictionary(e => (string)e.Name!, e => e.Value);
        var fileIdsJson = hash.TryGetValue(FieldFileIds, out var rawFileIds) && !rawFileIds.IsNullOrEmpty
            ? (string)rawFileIds!
            : "[]";

        return new ShareSession(
            code,
            hash.TryGetValue(FieldPresenterUserId, out var presenter) ? (string)presenter! : "",
            hash.TryGetValue(FieldState, out var state) ? (string)state! : ShareSessionStates.Live,
            hash.TryGetValue(FieldSeq, out var seq) ? (long)seq : 0,
            hash.TryGetValue(FieldSnapshot, out var snapshot) && !snapshot.IsNullOrEmpty ? (string)snapshot! : null,
            JsonSerializer.Deserialize<List<string>>(fileIdsJson) ?? [],
            hash.TryGetValue(FieldCreatedAt, out var createdAt)
                ? DateTime.Parse((string)createdAt!, null, System.Globalization.DateTimeStyles.RoundtripKind)
                : default);
    }

    public Task<bool> SessionExistsAsync(string code)
        => Db.KeyExistsAsync(ShareKeys.Session(code));

    public Task<bool> RefreshTtlAsync(string code, TimeSpan ttl)
        => Db.KeyExpireAsync(ShareKeys.Session(code), ttl);

    public async Task<bool> SetStateAsync(string code, string state)
    {
        var key = ShareKeys.Session(code);
        var transaction = Db.CreateTransaction();
        transaction.AddCondition(Condition.KeyExists(key));
        _ = transaction.HashSetAsync(key, [new HashEntry(FieldState, state)]);
        return await transaction.ExecuteAsync();
    }

    public async Task<bool> UpdateSessionAsync(string code, long seq, string? snapshotJson, IReadOnlyList<string>? fileIds, TimeSpan ttl)
    {
        var key = ShareKeys.Session(code);
        var fields = snapshotJson is null
            ? new[] { new HashEntry(FieldSeq, seq) }
            : new[]
            {
                new HashEntry(FieldSeq, seq),
                new HashEntry(FieldSnapshot, snapshotJson),
                new HashEntry(FieldFileIds, JsonSerializer.Serialize(fileIds ?? [])),
            };

        var transaction = Db.CreateTransaction();
        transaction.AddCondition(Condition.KeyExists(key));
        _ = transaction.HashSetAsync(key, fields);
        _ = transaction.KeyExpireAsync(key, ttl);
        return await transaction.ExecuteAsync();
    }

    public async Task DeleteSessionAsync(string code)
        => await Db.KeyDeleteAsync([ShareKeys.Session(code), ShareKeys.Viewers(code)]);

    public async Task AddViewerAsync(string code, string viewerId, TimeSpan ttl)
    {
        var key = ShareKeys.Viewers(code);
        var batch = Db.CreateBatch();
        var add = batch.SortedSetAddAsync(key, viewerId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var expire = batch.KeyExpireAsync(key, ttl);
        batch.Execute();
        await Task.WhenAll(add, expire);
    }

    public Task RemoveViewerAsync(string code, string viewerId)
        => Db.SortedSetRemoveAsync(ShareKeys.Viewers(code), viewerId);

    public async Task<int> GetViewerCountAsync(string code)
    {
        var key = ShareKeys.Viewers(code);
        var staleBefore = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ShareKeys.PresenceWindowSeconds;
        await Db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, staleBefore);
        return (int)await Db.SortedSetLengthAsync(key);
    }

    private static HashEntry[] ToHashEntries(ShareSession session)
    {
        var entries = new List<HashEntry>
        {
            new(FieldPresenterUserId, session.PresenterUserId),
            new(FieldState, session.State),
            new(FieldSeq, session.Seq),
            new(FieldFileIds, JsonSerializer.Serialize(session.FileIds)),
            new(FieldCreatedAt, session.CreatedAt.ToString("O")),
        };
        if (session.SnapshotJson is not null)
            entries.Add(new HashEntry(FieldSnapshot, session.SnapshotJson));
        return entries.ToArray();
    }
}

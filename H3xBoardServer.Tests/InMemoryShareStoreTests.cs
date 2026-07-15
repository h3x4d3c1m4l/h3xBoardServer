using H3xBoardServer.Services.Sharing;

namespace H3xBoardServer.Tests;

public class InMemoryShareStoreTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(180);

    private static ShareSession NewSession(string code = "ABC234", string userId = "user-1")
        => new(code, userId, ShareSessionStates.Live, 0, null, [], DateTime.UtcNow);

    [Fact]
    public async Task TryClaimCode_SecondClaimOfSameCodeFails()
    {
        var store = new InMemoryShareStore(new FakeTimeProvider());

        Assert.True(await store.TryClaimCodeAsync(NewSession(), Ttl));
        Assert.False(await store.TryClaimCodeAsync(NewSession(userId: "user-2"), Ttl));

        var session = await store.GetSessionAsync("ABC234");
        Assert.NotNull(session);
        Assert.Equal("user-1", session.PresenterUserId);  // the first claim won
    }

    [Fact]
    public async Task Session_ExpiresAfterTtl_AndCodeBecomesClaimableAgain()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryShareStore(time);
        await store.TryClaimCodeAsync(NewSession(), Ttl);

        time.Advance(TimeSpan.FromSeconds(179));
        Assert.True(await store.SessionExistsAsync("ABC234"));

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(await store.SessionExistsAsync("ABC234"));
        Assert.Null(await store.GetSessionAsync("ABC234"));
        Assert.False(await store.RefreshTtlAsync("ABC234", Ttl));
        Assert.False(await store.SetStateAsync("ABC234", ShareSessionStates.Paused));
        Assert.False(await store.UpdateSessionAsync("ABC234", 1, null, null, Ttl));

        // The expired code is claimable again.
        Assert.True(await store.TryClaimCodeAsync(NewSession(userId: "user-2"), Ttl));
    }

    [Fact]
    public async Task RefreshTtl_SlidesExpiry()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryShareStore(time);
        await store.TryClaimCodeAsync(NewSession(), Ttl);

        time.Advance(TimeSpan.FromSeconds(170));
        Assert.True(await store.RefreshTtlAsync("ABC234", Ttl));

        time.Advance(TimeSpan.FromSeconds(170));  // 340 s after claim, 170 s after refresh
        Assert.True(await store.SessionExistsAsync("ABC234"));
    }

    [Fact]
    public async Task SetState_DoesNotExtendTtl()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryShareStore(time);
        await store.TryClaimCodeAsync(NewSession(), Ttl);

        time.Advance(TimeSpan.FromSeconds(100));
        Assert.True(await store.SetStateAsync("ABC234", ShareSessionStates.Paused));
        Assert.Equal(ShareSessionStates.Paused, (await store.GetSessionAsync("ABC234"))!.State);

        // Pausing must not have refreshed the TTL — the original expiry still applies.
        time.Advance(TimeSpan.FromSeconds(81));
        Assert.False(await store.SessionExistsAsync("ABC234"));
    }

    [Fact]
    public async Task UpdateSession_StoresSeqSnapshotAndFileIds_AndSlidesTtl()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryShareStore(time);
        await store.TryClaimCodeAsync(NewSession(), Ttl);

        var snapshot = """{"v":1,"seq":5,"type":"snapshot","fileIds":["f1"]}""";
        Assert.True(await store.UpdateSessionAsync("ABC234", 5, snapshot, ["f1"], Ttl));

        var session = await store.GetSessionAsync("ABC234");
        Assert.NotNull(session);
        Assert.Equal(5, session.Seq);
        Assert.Equal(snapshot, session.SnapshotJson);
        Assert.Equal(["f1"], session.FileIds);

        // A seq-only update keeps the cached snapshot.
        Assert.True(await store.UpdateSessionAsync("ABC234", 6, null, null, Ttl));
        session = await store.GetSessionAsync("ABC234");
        Assert.Equal(6, session!.Seq);
        Assert.Equal(snapshot, session.SnapshotJson);
    }

    [Fact]
    public async Task Delete_RemovesSession()
    {
        var store = new InMemoryShareStore(new FakeTimeProvider());
        await store.TryClaimCodeAsync(NewSession(), Ttl);

        await store.DeleteSessionAsync("ABC234");
        Assert.False(await store.SessionExistsAsync("ABC234"));
        await store.DeleteSessionAsync("ABC234");  // idempotent
    }

    [Fact]
    public async Task ViewerPresence_CountsRecentViewers_AndPrunesStaleOnes()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryShareStore(time);
        await store.TryClaimCodeAsync(NewSession(), Ttl);

        await store.AddViewerAsync("ABC234", "viewer-1", Ttl);
        await store.AddViewerAsync("ABC234", "viewer-2", Ttl);
        Assert.Equal(2, await store.GetViewerCountAsync("ABC234"));

        // viewer-1 keeps pinging; viewer-2 goes silent and falls out of the presence window.
        time.Advance(TimeSpan.FromSeconds(30));
        await store.AddViewerAsync("ABC234", "viewer-1", Ttl);
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await store.GetViewerCountAsync("ABC234"));

        await store.RemoveViewerAsync("ABC234", "viewer-1");
        Assert.Equal(0, await store.GetViewerCountAsync("ABC234"));
    }

    [Fact]
    public async Task ViewerCount_IsZeroForUnknownSession()
    {
        var store = new InMemoryShareStore(new FakeTimeProvider());
        Assert.Equal(0, await store.GetViewerCountAsync("NOPE22"));
    }
}

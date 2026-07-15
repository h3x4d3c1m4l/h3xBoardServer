using H3xBoardServer.Services.Sharing;
using Microsoft.Extensions.Logging.Abstractions;

namespace H3xBoardServer.Tests;

public class ViewerRegistryTests
{
    private const string Code = "ABC234";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan LongWatchdog = TimeSpan.FromMinutes(5);  // inert during most tests

    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryShareStore _store;
    private readonly InMemoryShareBus _bus = new();

    public ViewerRegistryTests()
    {
        _store = new InMemoryShareStore(_time);
    }

    private ViewerRegistry CreateRegistry(int queueCapacity = 16, TimeSpan? watchdogInterval = null)
        => new(_bus, _store, NullLogger<ViewerRegistry>.Instance, queueCapacity, watchdogInterval ?? LongWatchdog);

    private Task ClaimSessionAsync()
        => _store.TryClaimCodeAsync(new ShareSession(Code, "user-1", ShareSessionStates.Live, 0, null, [], DateTime.UtcNow), Ttl);

    /// <summary>Drains a handle until its channel completes (or the timeout trips the test).</summary>
    private static async Task<List<string>> DrainAsync(ViewerHandle handle, int timeoutSeconds = 5)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var frames = new List<string>();
        await foreach (var frame in handle.Frames.ReadAllAsync(cts.Token))
            frames.Add(frame);
        return frames;
    }

    /// <summary>Reads one frame with a timeout so a broken relay fails the test instead of hanging it.</summary>
    private static async Task<string> ReadFrameAsync(ViewerHandle handle)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await handle.Frames.ReadAsync(cts.Token);
    }

    [Fact]
    public async Task DataFrames_FanOutToEveryLocalViewer_InOrder()
    {
        await ClaimSessionAsync();
        await using var registry = CreateRegistry();
        var viewer1 = await registry.RegisterAsync(Code, "viewer-1");
        var viewer2 = await registry.RegisterAsync(Code, "viewer-2");

        await _bus.PublishAsync(ShareKeys.DataChannel(Code), "frame-1");
        await _bus.PublishAsync(ShareKeys.DataChannel(Code), "frame-2");

        Assert.Equal("frame-1", await ReadFrameAsync(viewer1));
        Assert.Equal("frame-2", await ReadFrameAsync(viewer1));
        Assert.Equal("frame-1", await ReadFrameAsync(viewer2));
        Assert.Equal("frame-2", await ReadFrameAsync(viewer2));
    }

    [Fact]
    public async Task SlowViewer_OverflowsItsQueue_AndIsDropped()
    {
        await ClaimSessionAsync();
        await using var registry = CreateRegistry(queueCapacity: 2);
        var slow = await registry.RegisterAsync(Code, "viewer-slow");

        // Nothing reads from the handle: frame 3 cannot be enqueued and fails the viewer.
        await _bus.PublishAsync(ShareKeys.DataChannel(Code), "frame-1");
        await _bus.PublishAsync(ShareKeys.DataChannel(Code), "frame-2");
        await _bus.PublishAsync(ShareKeys.DataChannel(Code), "frame-3");

        Assert.True(slow.Overflowed);
        // The channel is completed: buffered frames drain, then the reader ends (frame-3 is lost).
        Assert.Equal(["frame-1", "frame-2"], await DrainAsync(slow));
    }

    [Fact]
    public async Task OneSlowViewer_DoesNotAffectTheOthers()
    {
        await ClaimSessionAsync();
        await using var registry = CreateRegistry(queueCapacity: 2);
        var slow = await registry.RegisterAsync(Code, "viewer-slow");
        var healthy = await registry.RegisterAsync(Code, "viewer-healthy");

        // The healthy viewer keeps draining its queue; the slow one never reads.
        for (var i = 0; i < 5; i++)
        {
            await _bus.PublishAsync(ShareKeys.DataChannel(Code), $"frame-{i}");
            Assert.Equal($"frame-{i}", await ReadFrameAsync(healthy));
        }

        Assert.True(slow.Overflowed);
        Assert.False(healthy.Overflowed);
    }

    [Fact]
    public async Task SessionEndedFrame_IsDeliveredAndClosesLocalViewers()
    {
        await ClaimSessionAsync();
        await using var registry = CreateRegistry();
        var viewer = await registry.RegisterAsync(Code, "viewer-1");

        await _bus.PublishAsync(ShareKeys.DataChannel(Code), "frame-1");
        await _bus.PublishAsync(ShareKeys.DataChannel(Code), ServerFrames.SessionEnded(SessionEndReasons.Stopped));

        var frames = await DrainAsync(viewer);  // completes because the entry was ended
        Assert.Equal(2, frames.Count);
        Assert.Equal("frame-1", frames[0]);
        Assert.Contains("\"type\":\"sessionEnded\"", frames[1]);
        Assert.Contains("\"reason\":\"stopped\"", frames[1]);
        Assert.False(viewer.Overflowed);
    }

    [Fact]
    public async Task Watchdog_EndsViewers_WhenTheSessionKeyExpires()
    {
        await ClaimSessionAsync();
        await using var registry = CreateRegistry(watchdogInterval: TimeSpan.FromMilliseconds(50));
        var viewer = await registry.RegisterAsync(Code, "viewer-1");

        _time.Advance(TimeSpan.FromMinutes(10));  // session TTL long gone

        var frames = await DrainAsync(viewer);  // watchdog notices within a tick or two
        var ended = Assert.Single(frames);
        Assert.Contains("\"type\":\"sessionEnded\"", ended);
        Assert.Contains("\"reason\":\"expired\"", ended);
    }

    [Fact]
    public async Task Watchdog_SurvivesStoreOutages_AndStillDetectsExpiryAfterwards()
    {
        await ClaimSessionAsync();
        var flaky = new FlakyStore(_store);
        await using var registry = new ViewerRegistry(
            _bus, flaky, NullLogger<ViewerRegistry>.Instance, 16, TimeSpan.FromMilliseconds(50));
        var viewer = await registry.RegisterAsync(Code, "viewer-1");

        // The store goes down mid-session (e.g. Redis restarting): the watchdog must keep
        // polling through the failures, and the viewers must be left alone.
        flaky.FailExistenceChecks = true;
        await Task.Delay(200);  // several failing ticks
        Assert.False(viewer.Frames.TryRead(out _));

        // The store recovers and reports the session gone: expiry is still detected.
        flaky.FailExistenceChecks = false;
        _time.Advance(TimeSpan.FromMinutes(10));
        var frames = await DrainAsync(viewer);
        var ended = Assert.Single(frames);
        Assert.Contains("\"type\":\"sessionEnded\"", ended);
        Assert.Contains("\"reason\":\"expired\"", ended);
    }

    /// <summary>Delegating store whose existence checks can be made to throw, simulating an outage.</summary>
    private sealed class FlakyStore(IShareStore inner) : IShareStore
    {
        public bool FailExistenceChecks { get; set; }

        public Task<bool> SessionExistsAsync(string code)
            => FailExistenceChecks
                ? throw new InvalidOperationException("store unavailable")
                : inner.SessionExistsAsync(code);

        public Task<bool> TryClaimCodeAsync(ShareSession session, TimeSpan ttl) => inner.TryClaimCodeAsync(session, ttl);
        public Task<ShareSession?> GetSessionAsync(string code) => inner.GetSessionAsync(code);
        public Task<bool> RefreshTtlAsync(string code, TimeSpan ttl) => inner.RefreshTtlAsync(code, ttl);
        public Task<bool> SetStateAsync(string code, string state) => inner.SetStateAsync(code, state);
        public Task<bool> UpdateSessionAsync(string code, long seq, string? snapshotJson, IReadOnlyList<string>? fileIds, TimeSpan ttl)
            => inner.UpdateSessionAsync(code, seq, snapshotJson, fileIds, ttl);
        public Task DeleteSessionAsync(string code) => inner.DeleteSessionAsync(code);
        public Task AddViewerAsync(string code, string viewerId, TimeSpan ttl) => inner.AddViewerAsync(code, viewerId, ttl);
        public Task RemoveViewerAsync(string code, string viewerId) => inner.RemoveViewerAsync(code, viewerId);
        public Task<int> GetViewerCountAsync(string code) => inner.GetViewerCountAsync(code);
    }

    [Fact]
    public async Task Unregister_LastViewer_DropsTheSubscription_AndReRegisterWorks()
    {
        await ClaimSessionAsync();
        await using var registry = CreateRegistry();
        var viewer = await registry.RegisterAsync(Code, "viewer-1");
        await registry.UnregisterAsync(Code, viewer);

        // No local viewers left: frames go nowhere but nothing breaks.
        await _bus.PublishAsync(ShareKeys.DataChannel(Code), "missed");

        var rejoined = await registry.RegisterAsync(Code, "viewer-2");
        await _bus.PublishAsync(ShareKeys.DataChannel(Code), "frame-1");
        Assert.Equal("frame-1", await ReadFrameAsync(rejoined));
        Assert.False(viewer.Frames.TryRead(out _));  // the unregistered viewer received nothing
    }
}

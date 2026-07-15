using System.Text.Json;
using H3xBoardServer.Rpc;
using H3xBoardServer.Rpc.Dtos;
using H3xBoardServer.Services.Sharing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace H3xBoardServer.Tests;

public class ShareSessionServiceTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryShareStore _store;
    private readonly InMemoryShareBus _bus = new();

    public ShareSessionServiceTests()
    {
        _store = new InMemoryShareStore(_time);
    }

    /// <summary>One presenter WebSocket connection: its scoped context/connection/service graph.</summary>
    private (ShareSessionService Service, RpcConnection Connection) CreateConnection(
        string userId = "user-1", long? maxMessageBytes = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sharing:SessionTtlSeconds"] = "180",
            ["Sharing:MaxMessageBytes"] = maxMessageBytes?.ToString(),
        }).Build();

        var context = new RpcContext();
        context.SetAuthenticated(userId, $"{userId}@example.com");
        var connection = new RpcConnection();
        var notifier = new PresenterNotifier(connection, _bus, NullLogger<PresenterNotifier>.Instance);
        var service = new ShareSessionService(
            _store, _bus, notifier, context, connection, configuration, NullLogger<ShareSessionService>.Instance);
        return (service, connection);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Start_ClaimsAValidCode_AndStoresTheSession()
    {
        var (service, connection) = CreateConnection();

        var result = await service.StartAsync(null);

        Assert.True(ShareCodes.IsValid(result.Code));
        Assert.Equal(0, result.ViewerCount);
        Assert.Equal(result.Code, connection.ShareCode);

        var session = await _store.GetSessionAsync(result.Code);
        Assert.NotNull(session);
        Assert.Equal("user-1", session.PresenterUserId);
        Assert.Equal(ShareSessionStates.Live, session.State);
    }

    [Fact]
    public async Task Start_IsIdempotentPerConnection()
    {
        var (service, _) = CreateConnection();

        var first = await service.StartAsync(null);
        var second = await service.StartAsync(null);

        Assert.Equal(first.Code, second.Code);
    }

    [Fact]
    public async Task Publish_WithoutASession_IsNotFound()
    {
        var (service, _) = CreateConnection();

        var ex = await Assert.ThrowsAsync<LocalRpcException>(
            () => service.PublishAsync([Json("""{"v":1,"seq":1,"type":"clear"}""")]));
        Assert.Equal(RpcErrors.CodeNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task Publish_RelaysFramesVerbatim_AndTracksSeqAndSnapshot()
    {
        var (service, connection) = CreateConnection();
        var started = await service.StartAsync(null);

        var relayed = new List<string>();
        await _bus.SubscribeAsync(ShareKeys.DataChannel(started.Code), relayed.Add);

        var snapshot = """{"v":1,"seq":1,"type":"snapshot","fileIds":["f1"],"board":{"widgets":[]}}""";
        var stroke = """{"v":1,"seq":2,"type":"strokeProgress","points":[[0,0],[5,5]]}""";
        await service.PublishAsync([Json(snapshot), Json(stroke)]);

        Assert.Equal([snapshot, stroke], relayed);

        var session = await _store.GetSessionAsync(started.Code);
        Assert.NotNull(session);
        Assert.Equal(2, session.Seq);              // last envelope's seq
        Assert.Equal(snapshot, session.SnapshotJson);
        Assert.Equal(["f1"], session.FileIds);
        Assert.Equal(connection.ShareCode, started.Code);
    }

    [Fact]
    public async Task Publish_OversizedBatch_IsRejectedWithPayloadTooLarge()
    {
        var (service, _) = CreateConnection(maxMessageBytes: 64);
        await service.StartAsync(null);

        var big = $$"""{"v":1,"seq":1,"type":"drawingSet","blob":"{{new string('x', 100)}}"}""";
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => service.PublishAsync([Json(big)]));
        Assert.Equal(RpcErrors.CodePayloadTooLarge, ex.ErrorCode);
    }

    [Fact]
    public async Task Publish_MalformedEnvelope_IsRejectedWithValidation()
    {
        var (service, _) = CreateConnection();
        await service.StartAsync(null);

        var ex = await Assert.ThrowsAsync<LocalRpcException>(
            () => service.PublishAsync([Json("""{"noType":true}""")]));
        Assert.Equal(RpcErrors.CodeValidation, ex.ErrorCode);
    }

    [Fact]
    public async Task Stop_DeletesTheSession_AndBroadcastsSessionEnded()
    {
        var (service, connection) = CreateConnection();
        var started = await service.StartAsync(null);

        var relayed = new List<string>();
        await _bus.SubscribeAsync(ShareKeys.DataChannel(started.Code), relayed.Add);

        await service.StopAsync();

        Assert.Null(connection.ShareCode);
        Assert.False(await _store.SessionExistsAsync(started.Code));
        var ended = Assert.Single(relayed);
        Assert.Contains("\"type\":\"sessionEnded\"", ended);
        Assert.Contains("\"reason\":\"stopped\"", ended);

        await service.StopAsync();  // idempotent
    }

    [Fact]
    public async Task Heartbeat_SlidesTheTtl()
    {
        var (service, _) = CreateConnection();
        var started = await service.StartAsync(null);

        _time.Advance(TimeSpan.FromSeconds(170));
        var result = await service.HeartbeatAsync();
        Assert.Equal(started.Code, result.Code);

        _time.Advance(TimeSpan.FromSeconds(170));  // would be expired without the heartbeat
        Assert.True(await _store.SessionExistsAsync(started.Code));
    }

    [Fact]
    public async Task Heartbeat_AfterExpiry_IsNotFound_AndUnbindsTheConnection()
    {
        var (service, connection) = CreateConnection();
        await service.StartAsync(null);

        _time.Advance(TimeSpan.FromSeconds(181));
        var ex = await Assert.ThrowsAsync<LocalRpcException>(() => service.HeartbeatAsync());
        Assert.Equal(RpcErrors.CodeNotFound, ex.ErrorCode);
        Assert.Null(connection.ShareCode);
    }

    [Fact]
    public async Task Disconnect_PausesTheSession_AndResumeRebindsIt()
    {
        // First connection: start, then drop.
        var (service1, _) = CreateConnection();
        var started = await service1.StartAsync(null);

        var relayed = new List<string>();
        await _bus.SubscribeAsync(ShareKeys.DataChannel(started.Code), relayed.Add);

        await service1.OnPresenterDisconnectedAsync();
        Assert.Equal(ShareSessionStates.Paused, (await _store.GetSessionAsync(started.Code))!.State);
        Assert.Contains(relayed, f => f.Contains("\"type\":\"sessionPaused\""));

        // Second connection (same user): resume within the TTL grace window.
        var (service2, connection2) = CreateConnection();
        var resumed = await service2.StartAsync(started.Code);

        Assert.Equal(started.Code, resumed.Code);
        Assert.Equal(started.Code, connection2.ShareCode);
        Assert.Equal(ShareSessionStates.Live, (await _store.GetSessionAsync(started.Code))!.State);
        Assert.Contains(relayed, f => f.Contains("\"type\":\"sessionResumed\""));
    }

    [Fact]
    public async Task Resume_WithAnotherUsersCode_StartsAFreshSessionInstead()
    {
        var (service1, _) = CreateConnection("user-1");
        var started = await service1.StartAsync(null);
        await service1.OnPresenterDisconnectedAsync();

        var (service2, _) = CreateConnection("user-2");
        var result = await service2.StartAsync(started.Code);

        Assert.NotEqual(started.Code, result.Code);
        // The original session is untouched (still paused, still user-1's).
        var original = await _store.GetSessionAsync(started.Code);
        Assert.NotNull(original);
        Assert.Equal("user-1", original.PresenterUserId);
        Assert.Equal(ShareSessionStates.Paused, original.State);
    }

    [Fact]
    public async Task Resume_NormalizesTheCode()
    {
        var (service1, _) = CreateConnection();
        var started = await service1.StartAsync(null);
        await service1.OnPresenterDisconnectedAsync();

        var (service2, _) = CreateConnection();
        var pretty = $"{started.Code[..3].ToLowerInvariant()}-{started.Code[3..].ToLowerInvariant()}";
        var resumed = await service2.StartAsync(pretty);

        Assert.Equal(started.Code, resumed.Code);
    }
}

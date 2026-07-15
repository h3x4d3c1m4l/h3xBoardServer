namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Publish/subscribe fan-out for live sharing, carrying raw string frames per channel (see
/// <see cref="ShareKeys.DataChannel"/> / <see cref="ShareKeys.ControlChannel"/>). Redis-backed in
/// production so frames reach viewers on every instance; in-process fallback for development.
/// Subscriptions are refcounted per channel per instance — implementations hold a single upstream
/// subscription per channel and dispatch to all local handlers.
/// </summary>
public interface IShareBus
{
    Task PublishAsync(string channel, string frame);

    /// <summary>
    /// Subscribes <paramref name="handler"/> to <paramref name="channel"/>. Handlers are invoked
    /// sequentially per channel in publish order and must not block — hand off to a queue for any
    /// real work. Dispose the returned subscription to unsubscribe.
    /// </summary>
    Task<IAsyncDisposable> SubscribeAsync(string channel, Action<string> handler);
}

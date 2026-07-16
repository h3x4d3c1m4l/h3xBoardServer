using System.Text.Json.Serialization;
using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

public static class WsEndpoints
{
    public static WebApplication MapWsEndpoints(this WebApplication app)
    {
        app.Map("/ws/v1", HandleV1);
        return app;
    }

    private static async Task HandleV1(HttpContext httpContext, IServiceProvider services, ILoggerFactory loggerFactory, IHostEnvironment env)
    {
        var logger = loggerFactory.CreateLogger(nameof(WsEndpoints));
        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("WebSocket connection required");
            return;
        }

        // LoadAsync must be called explicitly in the async WebSocket upgrade path —
        // the session middleware's auto-load does not fire here.
        await httpContext.Session.LoadAsync();
        var userId = httpContext.Session.GetString("userId");
        var email = httpContext.Session.GetString("email");

        if (userId is null || email is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response.WriteAsync("Authentication required");
            return;
        }

        var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
        var remoteIp = httpContext.Connection.RemoteIpAddress;
        logger.LogInformation("WebSocket connected from {Ip} as user {UserId}", remoteIp, userId);

        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var context = sp.GetRequiredService<RpcContext>();
        context.SetAuthenticated(userId, email);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new UtcDateTimeConverter() },
        };

        var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = jsonOptions };
        var handler = new WebSocketMessageHandler(webSocket, formatter);

        using var jsonRpc = new JsonRpc(handler);
        // Expose the JsonRpc instance to scoped services (e.g. the live-sharing PresenterNotifier)
        // so they can push server→client notifications on this connection.
        var connection = sp.GetRequiredService<RpcConnection>();
        connection.JsonRpc = jsonRpc;
        jsonRpc.AddLocalRpcTarget(sp.GetRequiredService<BoardsRpcV1>());
        jsonRpc.AddLocalRpcTarget(sp.GetRequiredService<FilesRpcV1>());
        jsonRpc.AddLocalRpcTarget(sp.GetRequiredService<SettingsRpcV1>());
        jsonRpc.AddLocalRpcTarget(sp.GetRequiredService<SharingRpcV1>());
        if (env.IsDevelopment())
            jsonRpc.AddLocalRpcTarget(sp.GetRequiredService<SystemRpcV1>());
        jsonRpc.StartListening();

        try
        {
            await jsonRpc.Completion;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "WebSocket error for {Ip}", remoteIp);
        }
        finally
        {
            // Pause any live share session so the presenter can resume within the TTL grace window.
            connection.JsonRpc = null;  // the connection is gone — don't attempt notifications
            try
            {
                await sp.GetRequiredService<ShareSessionService>().OnPresenterDisconnectedAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to pause share session on disconnect for {Ip}", remoteIp);
            }
        }

        logger.LogInformation("WebSocket disconnected from {Ip}", remoteIp);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using FluentMigrator.Runner;
using H3xBoardServer.Data.Migrations;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using StreamJsonRpc;

var builder = WebApplication.CreateBuilder(args);

// ── Database ────────────────────────────────────────────────────────────────
var dbSection = builder.Configuration.GetSection("Database");
var dbProvider = dbSection["Provider"] ?? "SQLite";
var dbConnStr = dbSection["ConnectionString"] ?? "Data Source=h3xboard.db";

DataOptions dataOptions = dbProvider.ToUpperInvariant() switch
{
    "SQLITE" => new DataOptions().UseSQLite(dbConnStr),
    // Future providers:
    // "MYSQL"      => new DataOptions().UseMySql(dbConnStr),
    // "POSTGRESQL" => new DataOptions().UsePostgreSQL(dbConnStr),
    _ => throw new InvalidOperationException($"Unsupported database provider: {dbProvider}"),
};

builder.Services.AddSingleton(dataOptions);
builder.Services.AddSingleton<H3xBoardDbFactory>();

// ── Migrations ──────────────────────────────────────────────────────────────
builder.Services.AddFluentMigratorCore()
    .ConfigureRunner(runner =>
    {
        var configured = dbProvider.ToUpperInvariant() switch
        {
            "SQLITE" => runner.AddSQLite(),
            // "MYSQL"      => runner.AddMySql5(),
            // "POSTGRESQL" => runner.AddPostgres(),
            _ => throw new InvalidOperationException($"Unsupported database provider: {dbProvider}"),
        };
        configured
            .WithGlobalConnectionString(dbConnStr)
            .ScanIn(typeof(M001_InitialSchema).Assembly).For.Migrations();
    })
    .AddLogging(lb => lb.AddFluentMigratorConsole());

// ── App services ────────────────────────────────────────────────────────────
builder.Services.AddScoped<RpcContext>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<AuthRpcV1>();
builder.Services.AddScoped<BoardsRpcV1>();

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ── Run migrations on startup ────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
}

// ── Middleware ───────────────────────────────────────────────────────────────
app.UseCors();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// ── Health check ─────────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

// ── WebSocket / JSON-RPC endpoint — v1 ───────────────────────────────────────
app.Map("/ws/v1", async (HttpContext httpContext, IServiceProvider services, ILogger<Program> logger) =>
{
    if (!httpContext.WebSockets.IsWebSocketRequest)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsync("WebSocket connection required");
        return;
    }

    var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
    var remoteIp = httpContext.Connection.RemoteIpAddress;
    logger.LogInformation("WebSocket connected from {Ip}", remoteIp);

    await using var scope = services.CreateAsyncScope();
    var sp = scope.ServiceProvider;

    var context = sp.GetRequiredService<RpcContext>();

    // Pre-authenticate if the client provides a JWT in the query string.
    // This lets already-authenticated clients skip the auth.login roundtrip.
    if (httpContext.Request.Query.TryGetValue("token", out var tokenValues))
    {
        try
        {
            var authService = sp.GetRequiredService<AuthService>();
            await authService.AuthenticateFromTokenAsync(tokenValues.ToString(), context);
            logger.LogInformation("Pre-authenticated user {User} via token", context.Username);
        }
        catch (Exception ex)
        {
            // Non-fatal — client can still authenticate via auth.login
            logger.LogWarning("Token pre-authentication failed: {Message}", ex.Message);
        }
    }

    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = jsonOptions };
    var handler = new WebSocketMessageHandler(webSocket, formatter);

    using var jsonRpc = new JsonRpc(handler);
    jsonRpc.AddLocalRpcTarget(sp.GetRequiredService<AuthRpcV1>());
    jsonRpc.AddLocalRpcTarget(sp.GetRequiredService<BoardsRpcV1>());
    jsonRpc.StartListening();

    try
    {
        await jsonRpc.Completion;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogError(ex, "WebSocket error for {Ip}", remoteIp);
    }

    logger.LogInformation("WebSocket disconnected from {Ip}", remoteIp);
});

app.Run();

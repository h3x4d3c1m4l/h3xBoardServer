using FluentMigrator.Runner;
using H3xBoardServer.Data.Migrations;
using Microsoft.AspNetCore.DataProtection;
using H3xBoardServer.Rest;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ////// //
// Sentry //
// ////// //

// Configured entirely from the "Sentry" section in appsettings (Dsn, TracesSampleRate, etc.) —
// see https://docs.sentry.io/platforms/dotnet/guides/aspnetcore/configuration/options/. When
// Sentry:Dsn is empty (the default), the SDK disables itself and this is a no-op.
builder.WebHost.UseSentry();

// //////// //
// Database //
// //////// //

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

// ////////// //
// Migrations //
// ////////// //

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

// //////////////////////////// //
// Distributed cache & key ring //
// //////////////////////////// //

// Dragonfly (Redis-compatible) backs both the distributed session cache and the DataProtection
// key ring, so sessions and the cookie-encryption keys survive restarts and are shared across
// instances. Configure via Redis:ConnectionString (e.g. "dragonfly:6379"). When empty, falls back
// to an in-process cache and default (filesystem) key storage — fine for local dev, but
// single-instance only and keys are lost on a container restart.
var redisConnStr = builder.Configuration["Redis:ConnectionString"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("H3xBoardServer");

if (!string.IsNullOrWhiteSpace(redisConnStr))
{
    // One multiplexer shared by the cache and the key ring (StackExchange.Redis is thread-safe).
    var redis = ConnectionMultiplexer.Connect(redisConnStr);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddStackExchangeRedisCache(opts =>
        opts.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis));
    dataProtection.PersistKeysToStackExchangeRedis(redis, "h3xboard:DataProtection-Keys");
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// //////// //
// Sessions //
// //////// //

var sessionDays = int.TryParse(builder.Configuration["Auth:SessionIdleTimeoutDays"], out var d) ? d : 30;
builder.Services.AddSession(opts =>
{
    opts.IdleTimeout = TimeSpan.FromDays(sessionDays);
    opts.Cookie.MaxAge = TimeSpan.FromDays(sessionDays);
    opts.Cookie.HttpOnly = true;
    opts.Cookie.IsEssential = true;
    opts.Cookie.SameSite = SameSiteMode.None;
    // SameSite=None requires the Secure attribute, so the cookie must always be marked Secure.
    // Chrome accepts Secure cookies over http://localhost (treated as a secure context), so this
    // works in development without HTTPS, and is also correct in production behind TLS.
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opts.Cookie.Name = ".h3xboard.session";
});

// //// //
// CORS //
// //// //

// Configure allowed origins in Cors:AllowedOrigins in appsettings.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

// AllowCredentials() (required for session cookies) is incompatible with AllowAnyOrigin().
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

// //////////// //
// App Services //
// //////////// //

builder.Services.AddScoped<RpcContext>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<BoardsRpcV1>();
builder.Services.AddScoped<SystemRpcV1>();

// File storage — swap the IFileStorage implementation here when adding S3/Azure backends
// (mirror the Database:Provider switch pattern, keyed on Storage:Backend). See docs/file-storage.md.
builder.Services.AddSingleton<IFileStorage, FileSystemFileStorage>();
builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<FilesRpcV1>();

builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<SettingsRpcV1>();

// Live board sharing — session state and fan-out go through Redis when configured, so share
// sessions work across instances and survive restarts (within their TTL). The in-process
// fallbacks are for local dev only; see the startup warning below and docs/live-sharing.md.
if (!string.IsNullOrWhiteSpace(redisConnStr))
{
    builder.Services.AddSingleton<IShareStore, RedisShareStore>();
    builder.Services.AddSingleton<IShareBus, RedisShareBus>();
}
else
{
    builder.Services.AddSingleton<IShareStore, InMemoryShareStore>();
    builder.Services.AddSingleton<IShareBus, InMemoryShareBus>();
}
builder.Services.AddSingleton<ViewerRegistry>();
builder.Services.AddSingleton<ShareCodeRateLimiter>();
builder.Services.AddScoped<RpcConnection>();
builder.Services.AddScoped<PresenterNotifier>();
builder.Services.AddScoped<ShareSessionService>();
builder.Services.AddScoped<SharingRpcV1>();

var app = builder.Build();

if (allowedOrigins.Length == 0)
    app.Logger.LogWarning("Cors:AllowedOrigins is empty — cross-origin requests will be rejected. Add allowed origins to appsettings.");

if (string.IsNullOrWhiteSpace(redisConnStr))
{
    app.Logger.LogWarning("Redis:ConnectionString is empty — using in-process session cache and filesystem DataProtection keys. This is single-instance only; sessions and keys do not survive a container restart. Set Redis:ConnectionString (e.g. a Dragonfly instance) for production.");
    app.Logger.LogWarning("Redis:ConnectionString is empty — live board sharing is using in-process session state and fan-out. This is single-instance only; share sessions do not survive a restart and viewers on other instances will not receive frames. Set Redis:ConnectionString (e.g. a Dragonfly instance) for production.");
}

// ////////////// //
// Run Migrations //
// ////////////// //

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
}

// ////////// //
// Middleware //
// ////////// //

app.UseCors();
app.UseSession();  // must be before UseWebSockets and route handlers

// Sliding expiry: ASP.NET Core only emits the session cookie when the session is first
// established, so a persistent Cookie.MaxAge gives a fixed window that expires even for an
// active user. Re-stamp the cookie on each authenticated request so its lifetime tracks the
// server-side sliding IdleTimeout.
app.Use(async (context, next) =>
{
    await next();

    if (!context.Response.HasStarted                                   // skip WS upgrades / started responses
        && context.Request.Cookies.TryGetValue(".h3xboard.session", out var sessionId)
        && !string.IsNullOrEmpty(sessionId)
        && context.Session.IsAvailable
        && context.Session.GetString("userId") is not null)            // null after logout's Session.Clear()
    {
        context.Response.Cookies.Append(".h3xboard.session", sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            IsEssential = true,
            Path = "/",
            MaxAge = TimeSpan.FromDays(sessionDays),
        });
    }
});

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// ////// //
// Routes //
// ////// //

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));
app.MapServerEndpoints();
app.MapAuthEndpoints();
app.MapFileEndpoints();
app.MapBoardScreenshotEndpoints();
app.MapViewFileEndpoints();
app.MapWsEndpoints();
app.MapViewWsEndpoints();

app.Run();

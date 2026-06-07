using FluentMigrator.Runner;
using H3xBoardServer.Data.Migrations;
using H3xBoardServer.Rest;
using H3xBoardServer.Rpc;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;

var builder = WebApplication.CreateBuilder(args);

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

// //////// //
// Sessions //
// //////// //

var sessionDays = int.TryParse(builder.Configuration["Auth:SessionIdleTimeoutDays"], out var d) ? d : 30;
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opts =>
{
    opts.IdleTimeout = TimeSpan.FromDays(sessionDays);
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

var app = builder.Build();

if (allowedOrigins.Length == 0)
    app.Logger.LogWarning("Cors:AllowedOrigins is empty — cross-origin requests will be rejected. Add allowed origins to appsettings.");

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
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// ////// //
// Routes //
// ////// //

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));
app.MapServerEndpoints();
app.MapAuthEndpoints();
app.MapWsEndpoints();

app.Run();

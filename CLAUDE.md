# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
dotnet build
dotnet run
dotnet run --environment Development   # uses h3xboard-dev.db
```

There are no automated tests yet. There is no linter configured beyond the IDE.

## Architecture

Single ASP.NET Core project (`H3xBoardServer.csproj`, net10.0). All entry-point wiring — DI registration, migration runner, REST endpoints, and WebSocket endpoint — lives in `Program.cs`.

### Request path

```
REST /api/v1/auth/*  →  Program.cs (minimal API lambdas)
                     →  AuthService (validates, sets HttpContext.Session)
                     →  H3xBoardDbFactory.Create() → H3xBoardDb (linq2db DataConnection)
                     →  SQLite

WebSocket /ws/v1     →  Program.cs (session auth gate — 401 if no valid session)
                     →  JsonRpc (StreamJsonRpc, SystemTextJsonFormatter, camelCase)
                     →  BoardsRpcV1   (RPC target class)
                     →  BoardService (business logic)
                     →  H3xBoardDbFactory.Create() → H3xBoardDb (linq2db DataConnection)
                     →  SQLite
```

Migrations run automatically at startup via `IMigrationRunner.MigrateUp()`.

### Key patterns

**Session-based auth** — Authentication is handled via REST endpoints (`/api/v1/auth/*`). A successful login or registration sets `userId` and `email` in the ASP.NET Core session and returns a `.h3xboard.session` cookie. The WebSocket endpoint reads this session at connection time; unauthenticated requests receive HTTP 401 before the WebSocket is accepted.

**Per-connection scope** — Each WebSocket connection gets its own `IServiceScope` (created with `CreateAsyncScope` in `Program.cs`). All scoped services — `RpcContext`, `BoardsRpcV1`, `BoardService` — live inside that scope and are disposed when the connection closes.

**`RpcContext`** (`Rpc/RpcContext.cs`) — Per-connection auth state, pre-populated from the session before JSON-RPC starts. `BoardsRpcV1` and `BoardService` read `RpcContext.UserId`/`Email` for the lifetime of the connection. `RequireAuthentication()` throws `LocalRpcException(4001)` as defense-in-depth (normally all WS connections are already authenticated).

**`H3xBoardDbFactory`** — Registered as singleton; each async operation calls `dbFactory.Create()` and disposes immediately with `await using`. This is intentional: `linq2db DataConnection` is not thread-safe and JSON-RPC allows concurrent in-flight calls on one connection.

**Board data blob** — The `boards.data` column is opaque JSON owned by the Flutter client. The server never parses it. `BoardService.MapToDto` does `JsonDocument.Parse(...).RootElement.Clone()` to return a `JsonElement` that owns its own memory independent of the parsed document lifetime.

**Error handling** — REST endpoints throw `AuthException` (defined in `AuthService.cs`) which carries an HTTP status code, caught by the endpoint lambda and mapped to `Results.Problem()`. WebSocket RPC errors throw `LocalRpcException` via helpers in `Rpc/RpcErrors.cs` (codes 4001–5000), propagated to the client as JSON-RPC error responses.

**Session storage** — `AddDistributedMemoryCache()` stores sessions in-process. This is correct for single-instance deployments. For multi-instance deployments, switch to `AddStackExchangeRedisCache()` or `AddSqlServerCache()`.

### Versioning model

Two independent axes:

- **Transport version** — the URL: `/ws/v1`, `/ws/v2`. Bump when the WebSocket handshake or RPC protocol itself changes.
- **Service version** — embedded in the method name: `boards.v2.list`. Bump when an individual service's API contract changes.

Wire method names follow `service.vN.action`. C# class names follow `*RpcVN`. Multiple class versions can be registered on the same `JsonRpc` instance simultaneously because their method names differ.

### Adding a new RPC endpoint version

1. Create `Rpc/BoardsRpcV2.cs` with `class BoardsRpcV2` and methods attributed `[JsonRpcMethod("boards.v2.*")]`
2. Register `builder.Services.AddScoped<BoardsRpcV2>()` in `Program.cs`
3. Add `jsonRpc.AddLocalRpcTarget(sp.GetRequiredService<BoardsRpcV2>())` inside the `/ws/v1` handler

### Adding a database provider

Uncomment the matching `case` blocks in `Program.cs` (two switch expressions — one for linq2db `DataOptions`, one for FluentMigrator runner), add the corresponding NuGet packages (`linq2db.*` and `FluentMigrator.Runner.*`), and set `Database:Provider` in config.

### Auth configuration

`Auth:SessionIdleTimeoutDays` in `appsettings.json` controls how long a session stays valid (default: 30 days). `Auth:AllowRegistration` (default: `true`) gates new sign-ups — when `false`, `AuthService.RegisterAsync` throws `AuthException(403)` and the unauthenticated `GET /api/v1/server/info` endpoint reports `registrationAllowed: false`. `Cors:AllowedOrigins` is an array of allowed origins — must be explicit (no wildcards) because `AllowCredentials()` is required for session cookies. In development, configure origins in `appsettings.Development.json`.

`/api/v1/server/info` (mapped in `Rest/ServerEndpoints.cs`) returns the unauthenticated `ServerInfo` capabilities object, designed to be extended over time. Both it and the registration guard read `Auth:AllowRegistration` directly from config, so the flag has a single source of truth.

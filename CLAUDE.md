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

Single ASP.NET Core project (`H3xBoardServer.csproj`, net10.0). All entry-point wiring — DI registration, migration runner, WebSocket endpoint — lives in `Program.cs`.

### Request path

```
WebSocket /ws/v1  →  Program.cs (per-connection AsyncScope)
                  →  JsonRpc (StreamJsonRpc, SystemTextJsonFormatter, camelCase)
                  →  AuthRpcV1 / BoardsRpcV1   (RPC target classes)
                  →  AuthService / BoardService (business logic)
                  →  H3xBoardDbFactory.Create() → H3xBoardDb (linq2db DataConnection)
                  →  SQLite
```

Migrations run automatically at startup via `IMigrationRunner.MigrateUp()`.

### Key patterns

**Per-connection scope** — Each WebSocket connection gets its own `IServiceScope` (created with `CreateAsyncScope` in `Program.cs`). All scoped services — `RpcContext`, `AuthRpcV1`, `BoardsRpcV1`, `AuthService`, `BoardService` — live inside that scope and are disposed when the connection closes.

**`RpcContext`** (`Rpc/RpcContext.cs`) — Mutable per-connection auth state. Both RPC service classes and both application services share the same `RpcContext` instance within a connection's scope. After `auth.v1.login` succeeds, `RpcContext.IsAuthenticated` becomes true and `RpcContext.CurrentReconnectToken` holds the session's active token for the lifetime of the connection. `RequireAuthentication()` throws `LocalRpcException(4001)` if the context is not yet authenticated.

**`H3xBoardDbFactory`** — Registered as singleton; each async operation calls `dbFactory.Create()` and disposes immediately with `await using`. This is intentional: `linq2db DataConnection` is not thread-safe and JSON-RPC allows concurrent in-flight calls on one connection.

**Board data blob** — The `boards.data` column is opaque JSON owned by the Flutter client. The server never parses it. `BoardService.MapToDto` does `JsonDocument.Parse(...).RootElement.Clone()` to return a `JsonElement` that owns its own memory independent of the parsed document lifetime.

**Error handling** — All expected errors throw `LocalRpcException` via helpers in `Rpc/RpcErrors.cs` (codes 4001–5000). These propagate to the client as JSON-RPC error responses. Unexpected exceptions in RPC methods will be swallowed by StreamJsonRpc's default policy (generic error to client, nothing logged) — wrap service calls in try/catch if you need logging.

### Versioning model

Two independent axes:

- **Transport version** — the URL: `/ws/v1`, `/ws/v2`. Bump when the WebSocket handshake or RPC protocol itself changes.
- **Service version** — embedded in the method name: `auth.v1.register`, `boards.v2.list`. Bump when an individual service's API contract changes.

Wire method names follow `service.vN.action`. C# class names follow `*RpcVN`. Multiple class versions can be registered on the same `JsonRpc` instance simultaneously (e.g. `AuthRpcV1` + `AuthRpcV2` both on `/ws/v1`) because their method names differ.

### Adding a new RPC endpoint version

1. Create `Rpc/AuthRpcV2.cs` with `class AuthRpcV2` and methods attributed `[JsonRpcMethod("auth.v2.*")]`
2. Register `builder.Services.AddScoped<AuthRpcV2>()` in `Program.cs`
3. Add `jsonRpc.AddLocalRpcTarget(sp.GetRequiredService<AuthRpcV2>())` inside the relevant `app.Map` handler

### Adding a database provider

Uncomment the matching `case` blocks in `Program.cs` (two switch expressions — one for linq2db `DataOptions`, one for FluentMigrator runner), add the corresponding NuGet packages (`linq2db.*` and `FluentMigrator.Runner.*`), and set `Database:Provider` in config.

### Auth configuration

`Auth:ReconnectTokenExpiryDays` in `appsettings.json` controls how long a reconnect token stays valid (default: 30 days). No secret key is required — auth is password-based (BCrypt) + random reconnect tokens stored in the `reconnect_tokens` table. There are no JWTs.

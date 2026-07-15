# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
dotnet build
dotnet run
dotnet run --environment Development   # uses h3xboard-dev.db
dotnet test H3xBoardServer.Tests/H3xBoardServer.Tests.csproj   # unit tests (xUnit)
```

Tests live in `H3xBoardServer.Tests/` (a subfolder of the root-level web project — the main csproj
excludes it from its default globs, keep that in place). Run `dotnet build` / `dotnet test` with an
explicit csproj argument when both matter; a bare `dotnet build` at the root builds only the server.
There is no linter configured beyond the IDE.

### Docker

`Dockerfile` is a multi-stage build (Microsoft .NET template) that runs as the non-root `$APP_UID` user, listens on port 8080, and keeps SQLite in the `/data` volume (`Database__ConnectionString` is set to `Data Source=/data/h3xboard.db` in the image). `.github/workflows/build-and-push-image.yml` builds and pushes `ghcr.io/<repo>:latest` and `:<sha>` to GHCR on every push to `main`. `docker-compose.yml` runs the server plus a Dragonfly (Redis-compatible) instance for restart-safe, multi-instance sessions. The `dragonfly` service is given `--snapshot_cron "*/5 * * * *"` + `--dbfilename dump` and a 30s `stop_grace_period` so it actually persists its in-memory data (session store **and** DataProtection key ring) to the `dragonfly-data` volume — without `--snapshot_cron` Dragonfly only flushes on graceful shutdown, so a force-killed/recreated Dragonfly container loses the key ring and every session cookie fails to decrypt.

```sh
docker compose up -d --build    # server + Dragonfly
```

## Architecture

Single ASP.NET Core project (`H3xBoardServer.csproj`, net10.0). All entry-point wiring — DI registration, migration runner, REST endpoints, and WebSocket endpoint — lives in `Program.cs`.

### Request path

```text
REST /api/v1/auth/*  →  Program.cs (minimal API lambdas)
                     →  AuthService (validates, sets HttpContext.Session)
                     →  H3xBoardDbFactory.Create() → H3xBoardDb (linq2db DataConnection)
                     →  SQLite

REST /api/v1/files   →  Rest/FileEndpoints.cs (session auth gate; upload=multipart, download=stream)
                     →  FileService → H3xBoardDb (files metadata)  +  IFileStorage (file bytes)

REST /api/v1/boards/{id}/screenshot
                     →  Rest/BoardScreenshotEndpoints.cs (session auth gate; PUT=upsert, GET=stream)
                     →  FileService → H3xBoardDb (board link + files metadata)  +  IFileStorage

WebSocket /ws/v1     →  Program.cs (session auth gate — 401 if no valid session)
                     →  JsonRpc (StreamJsonRpc, SystemTextJsonFormatter, camelCase)
                     →  BoardsRpcV1 / FilesRpcV1 / SettingsRpcV1 / SharingRpcV1   (RPC target classes)
                     →  BoardService / FileService / SettingsService / ShareSessionService (business logic)
                     →  H3xBoardDbFactory.Create() → H3xBoardDb (linq2db DataConnection)  +  IFileStorage (file bytes)
                     →  SQLite

WebSocket /ws/v1/view/{code}
                     →  Rpc/ViewWsEndpoints.cs (anonymous — share code is the credential; plain JSON, not JSON-RPC)
                     →  ViewerRegistry (local fan-out) ← IShareBus (Redis pub/sub)  +  IShareStore (session/presence)

REST /api/v1/view/{code}/files/{fileId}
                     →  Rest/ViewFileEndpoints.cs (anonymous; fileId must be in the session snapshot's fileIds)
                     →  FileService (owner-scoped, as the presenter)  +  IFileStorage
```

Migrations run automatically at startup via `IMigrationRunner.MigrateUp()`.

### Key patterns

**Session-based auth** — Authentication is handled via REST endpoints (`/api/v1/auth/*`). A successful login or registration sets `userId` and `email` in the ASP.NET Core session and returns a `.h3xboard.session` cookie. The WebSocket endpoint reads this session at connection time; unauthenticated requests receive HTTP 401 before the WebSocket is accepted.

**Per-connection scope** — Each WebSocket connection gets its own `IServiceScope` (created with `CreateAsyncScope` in `Program.cs`). All scoped services — `RpcContext`, `BoardsRpcV1`, `BoardService` — live inside that scope and are disposed when the connection closes.

**`RpcContext`** (`Rpc/RpcContext.cs`) — Per-connection auth state, pre-populated from the session before JSON-RPC starts. `BoardsRpcV1` and `BoardService` read `RpcContext.UserId`/`Email` for the lifetime of the connection. There is no per-method auth guard: the `/ws/v1` handler (`Rpc/WsEndpoints.cs`) is the single auth checkpoint — it returns HTTP 401 before accepting the WebSocket if the session is unauthenticated, so every RPC method runs with `UserId` already set (hence the `context.UserId!` usage).

**`H3xBoardDbFactory`** — Registered as singleton; each async operation calls `dbFactory.Create()` and disposes immediately with `await using`. This is intentional: `linq2db DataConnection` is not thread-safe and JSON-RPC allows concurrent in-flight calls on one connection.

**Board data blob** — The `boards.data` column is opaque JSON owned by the Flutter client. The server never parses it. `BoardService.MapToDto` does `JsonDocument.Parse(...).RootElement.Clone()` to return a `JsonElement` that owns its own memory independent of the parsed document lifetime.

**User settings** — Per-user preferences live one row per `(user_id, key)` in the `user_settings` table (composite PK), with the value stored as raw JSON text and round-tripped as a `JsonElement` (same `JsonDocument.Parse(...).RootElement.Clone()` trick as `boards.data`). Writes are **per-key patches** (`settings.v1.set` touches one key), so concurrent edits to different keys never clobber each other. The API is JSON-RPC only (`Rpc/SettingsRpcV1.cs` → `SettingsService`): `settings.v1.{getAll,get,set,delete}`. Unlike `boards.data`, the server can *read* settings: keys it understands are declared in `Settings/KnownSettings.cs` (a registry of key → type + default + optional validation). On write, a known key's value is type-checked; on read, a known-but-unset key falls back to its default (`settings.v1.get` and the server-internal typed accessor `SettingsService.GetValueAsync<T>`). Keys absent from the registry are still allowed and stored verbatim as a client-owned bag. `getAll` returns only stored rows (no synthesized defaults). Value size and per-user key count are capped by `Settings:MaxValueBytes` / `Settings:MaxKeysPerUser`; keys are validated (charset `[A-Za-z0-9._-]`, ≤128 chars).

**File storage** — Binary files (first use: board backgrounds) split into two layers: metadata in the `files` table (linq2db) and bytes in a pluggable `IFileStorage` backend (`Storage/`, `FileSystemFileStorage` today; S3/Azure are drop-in via the `Storage:Backend` switch in `Program.cs`). The DB is the source of truth for browsing and access control — every `FileService` query is owner-scoped — so listing never relies on a backend API. Files are owner-scoped via `(owner_scope, owner_id)`: `owner_scope` is always `"user"` today (key prefix `users/{userId}/...`), with a future `"company"` scope (`companies/{companyId}/...`) needing only a new scope value + authorization rule, no schema change. **Physical** storage keys are server-generated UUIDs (`{scope}/{ownerId}/{fileId}`) so no client input touches the path; on top of that each row carries a **virtual** location — `path` (folder, `""` = root) + `file_name` (leaf) — decoupled from the key, so move/rename is metadata-only. `FileService` normalizes/validates client-supplied `path` (no `..`, etc.). The API is split by payload type: metadata operations (`files.v1.browse`/`files.v1.delete`, `Rpc/FilesRpcV1.cs`) are JSON-RPC over the WebSocket — `browse` lists one folder (sub-folders + files) — while the **bytes** are uploaded/downloaded over REST (`Rest/FileEndpoints.cs`: `POST /api/v1/files` multipart, `GET /api/v1/files/{id}` streamed) so binary streams natively instead of base64. Upload is capped by `Storage:MaxUploadBytes`. `FileService` is shared by both transports and throws `RpcErrors` (HTTP-status-aligned codes); the REST endpoints map those codes back to HTTP status via `MapStatus`. Every row has a `kind` (`FileKind`, stored as a string): `user` (the only kind `browse`/`delete` ever see) vs system kinds like `board-screenshot` that are hidden from the generic API. See `docs/file-storage.md`.

**Board screenshots** — A board has at most one screenshot (a hidden `FileKind.BoardScreenshot` file linked 1:1 from `boards.screenshot_file_id`), uploaded by the Flutter client out-of-band from board-data saves. Bytes ride REST like other files but on a board-scoped route (`Rest/BoardScreenshotEndpoints.cs`: `PUT /api/v1/boards/{id}/screenshot` upsert, `GET` stream). `FileService.SetBoardScreenshotAsync` is an **upsert** — re-uploads overwrite the bytes in place (stable id + storage key, no orphan accumulation) and deliberately do **not** bump `boards.updated_at` (so periodic screenshots don't reorder `boards.v1.list`). `BoardSummary`/`BoardDto` expose `hasScreenshot`; deleting a board cascade-deletes its screenshot.

**Error handling** — REST endpoints throw `AuthException` (defined in `AuthService.cs`) which carries an HTTP status code, caught by the endpoint lambda and mapped to `Results.Problem()`. WebSocket RPC errors throw `LocalRpcException` via helpers in `Rpc/RpcErrors.cs` (HTTP-status-aligned codes in the 4000-5000 range, e.g. 4004 not-found, 4022 validation), propagated to the client as JSON-RPC error responses. Any other (unexpected) exception escaping a method surfaces with StreamJsonRpc's default invocation-error code `-32000`, not a custom code. The Development-only `SystemRpc` (`Rpc/SystemRpc.cs`, registered in `Rpc/WsEndpoints.cs` only when `env.IsDevelopment()`) exposes `system.v1.throw` to exercise that path.

**Session storage & DataProtection** — When `Redis:ConnectionString` is set (e.g. a Dragonfly instance), `Program.cs` shares one `IConnectionMultiplexer` between `AddStackExchangeRedisCache()` (the session store) and `PersistKeysToStackExchangeRedis()` (the DataProtection key ring keyed `h3xboard:DataProtection-Keys`). This makes sessions and the cookie-encryption keys both restart-safe and multi-instance. When the connection string is empty, it falls back to `AddDistributedMemoryCache()` + filesystem keys — single-instance and lost on restart (a startup warning is logged). `SetApplicationName("H3xBoardServer")` is always set so the key ring is stable. Note: keys are stored unencrypted at rest (the `XmlKeyManager` "No XML encryptor configured" log) — encryption at rest would require `ProtectKeysWithCertificate` and a provisioned cert.

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

`/api/v1/server/info` (mapped in `Rest/ServerEndpoints.cs`) returns the unauthenticated `ServerInfo` capabilities object, designed to be extended over time — currently `registrationAllowed`, `maxUploadBytes`, and `warning`. Both it and the registration guard read `Auth:AllowRegistration` directly from config, so the flag has a single source of truth. `Server:Warning` is an optional server-wide banner string (empty/whitespace ⇒ `null` in the response) that clients should surface prominently in their UI — e.g. to flag a testing-only environment where data loss may occur.

# JSON-RPC methods

Authentication is handled via REST — see [connecting-and-auth-flow.md](connecting-and-auth-flow.md). All methods below require an authenticated WebSocket connection (the server enforces this at connection time via the session cookie).

All requests follow JSON-RPC 2.0:

```json
{ "jsonrpc": "2.0", "method": "boards.v1.list", "id": 1, "params": {} }
```

## Boards

All board methods require authentication.

| Method | Description |
| --- | --- |
| `boards.v1.list` | List all boards (summary only, no data blob), most-recently-updated first |
| `boards.v1.get` | Fetch one board including full JSON data blob |
| `boards.v1.create` | Create a board |
| `boards.v1.update` | Partial update — omit any field to leave it unchanged |
| `boards.v1.delete` | Permanently delete a board (no undo) |

**boards.v1.list** response:

```json
{ "result": [ { "id": "uuid", "title": "My Board", "hasScreenshot": true, "createdAt": "...", "updatedAt": "..." } ] }
```

`hasScreenshot` reports whether `GET /api/v1/boards/{id}/screenshot` will return an image (also present
on `boards.v1.get`); see [file-storage.md](file-storage.md#board-screenshots).

**boards.v1.get** request:

```json
{ "params": { "id": "uuid" } }
```

**boards.v1.create** request:

```json
{ "params": { "title": "My Board", "data": { "backgroundColor": 4294967295, "widgets": [] } } }
```

**boards.v1.update** request — send only what you want to change:

```json
{ "params": { "id": "uuid", "title": "Renamed" } }
{ "params": { "id": "uuid", "data": { ... } } }
{ "params": { "id": "uuid", "title": "New name", "data": { ... } } }
```

**boards.v1.delete** request:

```json
{ "params": { "id": "uuid" } }
```

## Files

All file methods require authentication and operate on the authenticated user's files. Bytes are
carried inline as base64. See [file-storage.md](file-storage.md) for the storage model and size limit.

Files live in **virtual folders**: each file has a `path` (the folder, `""` = root) and a `fileName`
(the leaf). The path is decoupled from physical storage — see [file-storage.md](file-storage.md).

> **Bytes are not on the WebSocket.** Only the metadata operations below are JSON-RPC. Uploading and
> downloading the file bytes are **REST** (`POST` / `GET /api/v1/files`) so binary streams natively —
> see [file-storage.md](file-storage.md#rest-endpoints).

| Method | Description |
| --- | --- |
| `files.v1.browse` | List one folder — the immediate sub-folders and the files directly in it (metadata only, no bytes) |
| `files.v1.delete` | Permanently delete a file — bytes and metadata (no undo) |

**files.v1.browse** request — `path` is optional (omit or `""` for the root folder):

```json
{ "params": { "path": "boards/123" } }
{ "params": {} }
```

**files.v1.browse** response — sub-folders (names) plus the files directly in `path`:

```json
{ "result": {
  "path": "boards/123",
  "folders": ["backgrounds"],
  "files": [ { "id": "uuid", "path": "boards/123", "fileName": "notes.txt", "contentType": "text/plain", "sizeBytes": 20480, "createdAt": "...", "updatedAt": "..." } ]
} }
```

**files.v1.delete** request:

```json
{ "params": { "id": "uuid" } }
```

**files.v1.delete** request:

```json
{ "params": { "id": "uuid" } }
```

## Settings

Per-user preferences, one value per `key`. All settings methods require authentication and operate on
the authenticated user's settings. Writes are **per-key patches** — each call touches a single key, so
two devices editing different keys never clobber each other. A value is any JSON shape (bool, string,
number, object, array). See [file-storage.md](file-storage.md) for the broader storage model; settings
themselves are metadata-only (no bytes, no REST).

Keys the **server** reads (e.g. `notifications.email`, `ui.theme`) are declared server-side with a type
and a default: their value is type-checked on write, and a known-but-unset key returns its default on
`settings.v1.get`. Any other key is allowed and stored verbatim as a client-owned value. Keys are limited
to `[A-Za-z0-9._-]` (≤128 chars); value size and per-user key count are capped server-side.

| Method | Description |
| --- | --- |
| `settings.v1.getAll` | List all of the user's stored settings (does **not** include defaults for unset known keys) |
| `settings.v1.get` | Get one setting; a known-but-unset key returns its server default, an unknown unset key is `4004` not-found |
| `settings.v1.set` | Upsert one key to a value (per-key patch) |
| `settings.v1.delete` | Remove one key (a known key then reverts to its server default) |

**settings.v1.getAll** request / response:

```json
{ "params": {} }
{ "result": [ { "key": "ui.theme", "value": "dark", "updatedAt": "..." } ] }
```

**settings.v1.get** request — `updatedAt` is the epoch (`0001-01-01T00:00:00`) when the value is a served default:

```json
{ "params": { "key": "ui.theme" } }
{ "result": { "key": "ui.theme", "value": "dark", "updatedAt": "..." } }
```

**settings.v1.set** request — `value` is any JSON value:

```json
{ "params": { "key": "ui.theme", "value": "dark" } }
{ "params": { "key": "editor.grid", "value": { "size": 24, "snap": true } } }
```

A bad key, an over-size value, or a wrong-typed value for a server-known key returns `4022` validation —
see [error-codes.md](error-codes.md).

**settings.v1.delete** request:

```json
{ "params": { "key": "ui.theme" } }
```

## Diagnostics (Development only)

These methods are registered only when the server runs in the Development environment.

| Method | Description |
| --- | --- |
| `system.v1.throw` | Deliberately throws an unhandled exception. Use it to inspect how unexpected server errors surface — see [error-codes.md](error-codes.md) (they return JSON-RPC code `-32000`, not a custom code). |

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

## Live sharing

Presenter-side control of live board sharing. All sharing methods require authentication and operate
on **this connection's** share session (a connection presents at most one session at a time; the
session pauses when the connection drops and can be resumed on the next one). Viewers do **not** use
JSON-RPC — they connect anonymously to the plain-JSON WebSocket `/ws/v1/view/{code}`. The published
message payloads are opaque to the server: only the envelope fields `type`, `seq` and (for snapshots)
`fileIds` are read. See [live-sharing.md](live-sharing.md) for the full architecture, envelope
contract, and viewer protocol.

| Method | Description |
| --- | --- |
| `sharing.v1.start` | Start a session and get its 6-char code. Idempotent; pass `resumeCode` to re-bind a paused session after a reconnect |
| `sharing.v1.stop` | End the session — viewers receive `sessionEnded {reason:"stopped"}` (no undo) |
| `sharing.v1.publish` | Publish a batch of opaque envelopes to the viewers (the hot path — called ~20×/s while drawing) |
| `sharing.v1.heartbeat` | Slide the session TTL; call every ~30 s while sharing, even when idle |

**sharing.v1.start** request / response — `resumeCode` is optional (dashes/whitespace/case are tolerated):

```json
{ "params": {} }
{ "params": { "resumeCode": "AB-C23-4" } }
{ "result": { "code": "ABC234", "viewerCount": 0 } }
```

An unknown, expired, or foreign `resumeCode` silently falls back to creating a fresh session — check
`code` in the result to know which happened.

**sharing.v1.publish** request — `messages` is a batch of envelopes, relayed verbatim and in order.
Each envelope must carry `type` (non-empty string) and `seq` (integer); a `snapshot` envelope may
carry `fileIds` (string array) and is additionally cached for late-joining viewers:

```json
{ "params": { "messages": [
  { "v": 1, "seq": 12, "type": "snapshot", "fileIds": ["uuid"], "board": { "...": "opaque" } },
  { "v": 1, "seq": 13, "type": "strokeProgress", "...": "opaque" }
] } }
```

A malformed envelope rejects the whole batch with `4022`; a batch over `Sharing:MaxMessageBytes`
(default 512 KB) rejects with `4013`; publishing without a session (or after it expired) is `4004` —
see [error-codes.md](error-codes.md).

**sharing.v1.heartbeat** request / response:

```json
{ "params": {} }
{ "result": { "code": "ABC234", "viewerCount": 3 } }
```

**sharing.v1.stop** request:

```json
{ "params": {} }
```

### Server → presenter notifications

While a session is active the server pushes JSON-RPC **notifications** (no `id`, no response) on the
presenter's connection:

| Notification | Params | When |
| --- | --- | --- |
| `sharing.v1.viewerCount` | `{ "count": 3 }` | The viewer count changed or viewers pinged (debounced to ≤1/s) |
| `sharing.v1.snapshotRequested` | *(none)* | A viewer joined or asked for a resync — publish a fresh `snapshot` (debounced to ≤1 per 250 ms) |
| `sharing.v1.ended` | `{ "reason": "expired" }` | The session vanished under a live connection (e.g. TTL expiry) |

```json
{ "jsonrpc": "2.0", "method": "sharing.v1.viewerCount", "params": { "count": 3 } }
{ "jsonrpc": "2.0", "method": "sharing.v1.snapshotRequested" }
{ "jsonrpc": "2.0", "method": "sharing.v1.ended", "params": { "reason": "expired" } }
```

## Diagnostics (Development only)

These methods are registered only when the server runs in the Development environment.

| Method | Description |
| --- | --- |
| `system.v1.throw` | Deliberately throws an unhandled exception. Use it to inspect how unexpected server errors surface — see [error-codes.md](error-codes.md) (they return JSON-RPC code `-32000`, not a custom code). |

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
{ "result": [ { "id": "uuid", "title": "My Board", "createdAt": "...", "updatedAt": "..." } ] }
```

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

## Diagnostics (Development only)

These methods are registered only when the server runs in the Development environment.

| Method | Description |
| --- | --- |
| `system.v1.throw` | Deliberately throws an unhandled exception. Use it to inspect how unexpected server errors surface — see [error-codes.md](error-codes.md) (they return JSON-RPC code `-32000`, not a custom code). |

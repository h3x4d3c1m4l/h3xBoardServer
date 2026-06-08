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

## Diagnostics (Development only)

These methods are registered only when the server runs in the Development environment.

| Method | Description |
| --- | --- |
| `system.v1.throw` | Deliberately throws an unhandled exception. Use it to inspect how unexpected server errors surface — see [error-codes.md](error-codes.md) (they return JSON-RPC code `-32000`, not a custom code). |

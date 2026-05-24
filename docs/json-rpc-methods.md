# JSON-RPC methods

All requests follow JSON-RPC 2.0:
```json
{ "jsonrpc": "2.0", "method": "auth.login", "id": 1, "params": { ... } }
```

## Auth

| Method | Auth required | Description |
|---|---|---|
| `auth.v1.register` | No | Create account, returns tokens |
| `auth.v1.login` | No | Sign in, returns tokens + marks connection authenticated |
| `auth.v1.refreshToken` | No | Rotate refresh token, returns new access token |
| `auth.v1.logout` | Yes | Revoke all refresh tokens |
| `auth.v1.whoami` | Yes | Returns `{ userId, username }` |

**auth.v1.register / auth.v1.login** — request:
```json
{ "jsonrpc": "2.0", "method": "auth.v1.register", "id": 1,
  "params": { "username": "alice", "email": "alice@example.com", "password": "secret123" } }
```
Response:
```json
{ "jsonrpc": "2.0", "id": 1,
  "result": { "accessToken": "...", "refreshToken": "...",
              "accessTokenExpiresInSeconds": 3600, "userId": 1, "username": "alice" } }
```

**auth.refreshToken** — request:
```json
{ "params": { "refreshToken": "<opaque-token>" } }
```

## Boards

All board methods require authentication.

| Method | Description |
|---|---|
| `boards.v1.list` | List all boards (summary only, no data blob), most-recently-updated first |
| `boards.v1.get` | Fetch one board including full JSON data blob |
| `boards.v1.create` | Create a board |
| `boards.v1.update` | Partial update — omit any field to leave it unchanged |
| `boards.v1.delete` | Permanently delete a board (no undo) |

**boards.v1.list** response:
```json
{ "result": [ { "id": "uuid", "title": "My Board", "createdAt": "...", "updatedAt": "..." } ] }
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

# Connecting

```
ws://<host>/ws/v1
```

To skip `auth.login` on reconnect, pass the access token as a query param — the server pre-authenticates before the first RPC call:

```
ws://<host>/ws/v1?token=<jwt>
```

Authentication state is per-connection and lives until the WebSocket is closed.

# Auth flow

```
Client                               Server
  │                                    │
  ├─── WS connect /ws/v1 ──────────► │
  │                                    │
  ├─── auth.register ───────────────► │ creates user, issues tokens
  │    or auth.login                   │
  │◄── { accessToken, refreshToken } ──┤ connection is now authenticated
  │                                    │
  ├─── boards.list ─────────────────► │ works because connection is authenticated
  │◄── [ { id, title, ... }, ... ] ───┤
  │                                    │
  ├─── auth.refreshToken ───────────► │ rotates the refresh token
  │◄── { accessToken } ───────────────┤
  │                                    │
  └─── WS disconnect ───────────────► │
  │                                    │
  ├─── WS connect /ws/v1?token=<jwt> ►│ pre-authenticated immediately
  │◄── (no extra call needed) ─────────┤
```

# Connecting and authentication flow

## Connecting

Connect to the WebSocket endpoint:

```text
ws://<host>/ws/v1
```

No token is passed on connect. Authentication happens as the first RPC call after the connection is established.

## Registration

Call `auth.v1.register` with an email and password. Registration immediately authenticates the connection — no separate login call is needed.

```json
{ "jsonrpc": "2.0", "method": "auth.v1.register", "id": 1,
  "params": { "email": "alice@example.com", "password": "secret123" } }
```

```json
{ "jsonrpc": "2.0", "id": 1,
  "result": {
    "reconnectToken": "<opaque-token>",
    "userId": 1,
    "email": "alice@example.com"
  }
}
```

Validation rules:

- `email` must be non-empty
- `password` must be at least 8 characters
- `email` must not already be registered (error code 4009 if taken)

## Login

Call `auth.v1.login` with the registered email and password.

```json
{ "jsonrpc": "2.0", "method": "auth.v1.login", "id": 1,
  "params": { "email": "alice@example.com", "password": "secret123" } }
```

Response is the same shape as registration. On success, the **current WebSocket connection** is marked as authenticated — all subsequent calls on the same connection work without passing a token again.

Wrong email or password both return error code 4002. The same error is used for both so the caller cannot distinguish which field was wrong.

## Authenticated calls

Authentication state lives on the connection, not per-call. Once `auth.v1.login` or `auth.v1.register` succeeds, every call that requires authentication just works for the lifetime of that connection. When the connection closes, all state is discarded.

To verify the current auth state:

```json
{ "jsonrpc": "2.0", "method": "auth.v1.whoami", "id": 2, "params": [] }
```

```json
{ "result": { "userId": 1, "email": "alice@example.com" } }
```

## Reconnecting

When the connection drops and the client reconnects, it must re-authenticate. Two options:

**Option A — re-login with password** (if the client doesn't persist tokens):

```json
{ "method": "auth.v1.login", "params": { "email": "...", "password": "..." } }
```

**Option B — reconnect token** (preferred; avoids sending the password again):

```json
{ "jsonrpc": "2.0", "method": "auth.v1.reconnect", "id": 1,
  "params": { "reconnectToken": "<stored-token>" } }
```

```json
{ "result": { "reconnectToken": "<new-token>" } }
```

Reconnect tokens are **single-use** — each call to `auth.v1.reconnect` revokes the sent token and issues a new one. Store the new token immediately. Tokens expire after 30 days (configurable via `Auth:ReconnectTokenExpiryDays`).

`auth.v1.reconnect` also authenticates the current connection as a side effect, so there is no need to call `auth.v1.login` afterwards.

## Logout

Requires authentication. Revokes **only the current session's** reconnect token — other logged-in devices are unaffected.

```json
{ "jsonrpc": "2.0", "method": "auth.v1.logout", "id": 1, "params": [] }
```

## Token storage recommendations (client-side)

- Store the **reconnect token** in secure storage (e.g. Flutter's `flutter_secure_storage`).
- On app start: load the reconnect token from secure storage, call `auth.v1.reconnect` to get a fresh token and authenticate the connection.
- On logout: delete the stored token from secure storage in addition to calling `auth.v1.logout`.

# Connecting and authentication flow

Authentication uses standard HTTP REST endpoints with ASP.NET Core sessions. The client registers or logs in via REST, receives a session cookie, and then presents that cookie when opening the WebSocket connection.

## Step 0 — Discover server capabilities (REST, unauthenticated)

```http
GET /api/v1/server/info
```

Returns `200` with a small, unauthenticated capabilities object that clients can read before logging in:

```json
{ "registrationAllowed": true, "maxUploadBytes": 10485760, "warning": null }
```

This object is intended to grow over time. The fields are:

- `registrationAllowed` — whether the server accepts new registrations; clients should hide or disable their sign-up UI when it is `false`.
- `maxUploadBytes` — the maximum file upload size in bytes, so clients can validate before sending.
- `warning` — an optional server-wide banner message (`null` when unset). When non-null, clients should surface it prominently in their UI — e.g. to flag a testing-only environment where data loss may occur. Configured server-side via `Server:Warning` (empty/whitespace ⇒ `null`).

## Step 1 — Register or log in (REST)

**Register:**

```http
POST /api/v1/auth/register
Content-Type: application/json

{ "email": "alice@example.com", "password": "secret123" }
```

**Login:**

```http
POST /api/v1/auth/login
Content-Type: application/json

{ "email": "alice@example.com", "password": "secret123" }
```

Both return `200`/`201` with a JSON body and set the `.h3xboard.session` cookie:

```json
{ "userId": 1, "email": "alice@example.com" }
```

Validation rules:

- `email` must be non-empty
- `password` must be at least 8 characters
- `email` must not already be registered (`409` if taken)
- Wrong email or password both return `401` (no distinction between the two)
- Registration returns `403` when disabled by the server (`Auth:AllowRegistration` is `false`); login is unaffected

Registration can be turned off server-side via `Auth:AllowRegistration` in `appsettings.json` (default `true`). When disabled, `/api/v1/server/info` reports `"registrationAllowed": false` and the register endpoint rejects all requests with `403`.

## Step 2 — Connect to the WebSocket

```text
ws://<host>/ws/v1
```

The browser or HTTP client must send the `.h3xboard.session` cookie with the upgrade request (browsers do this automatically; Dart's `http` package requires a cookie jar). The server checks the session at connection time — if no valid session is found, the WebSocket is **rejected with HTTP 401** before any RPC traffic is exchanged.

## Step 3 — Use the board API

Once connected, authentication state lives on the connection for its lifetime. All `boards.v1.*` methods are available immediately. See [json-rpc-methods.md](json-rpc-methods.md) for the full method reference.

## Checking auth state (REST)

```http
GET /api/v1/auth/whoami
```

Returns `200 { "userId": 1, "email": "alice@example.com" }` if a valid session cookie is present, or `401` if not.

## Logout (REST)

```http
POST /api/v1/auth/logout
```

Clears the server-side session. The client should also discard the cookie. Returns `204 No Content`.

## Session lifetime

Sessions idle-expire after 30 days (configurable via `Auth:SessionIdleTimeoutDays`). Activity on any REST endpoint or WebSocket connection resets the idle timer. No manual token rotation is required — the session cookie is managed automatically.

## CORS and cookies

The session cookie uses `SameSite=None` to support cross-origin clients (e.g. Flutter Web served from a different port). Allowed origins must be listed explicitly in `Cors:AllowedOrigins` in `appsettings.json` — wildcard origins are incompatible with `AllowCredentials()`. In development, add your Flutter dev server origin to `appsettings.Development.json`.

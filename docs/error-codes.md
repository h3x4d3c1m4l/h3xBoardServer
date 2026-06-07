# Error codes

## REST endpoints (`/api/v1/auth/*`)

Errors are returned as `application/problem+json` (RFC 7807).

| HTTP status | Meaning |
|---|---|
| 400 | Validation error (e.g. password too short) |
| 401 | Invalid credentials or session not found |
| 409 | Conflict — email is already registered |

## JSON-RPC (over WebSocket `/ws/v1`)

Errors follow the JSON-RPC 2.0 error object format with a custom `code` field.

| Code | Meaning |
|---|---|
| 4001 | Unauthenticated (defense-in-depth; normally enforced at connection time) |
| 4004 | Not found |
| 4022 | Validation error |
| 5000 | Internal server error |

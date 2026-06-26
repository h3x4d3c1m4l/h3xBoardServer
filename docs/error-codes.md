# Error codes

## REST endpoints (`/api/v1/auth/*`, `/api/v1/files`)

Errors are returned as `application/problem+json` (RFC 7807).

| HTTP status | Meaning |
| --- | --- |
| 400 | Validation error (e.g. password too short; a file upload with an invalid path/filename, empty body, or over `Storage:MaxUploadBytes`) |
| 401 | Invalid credentials or session not found |
| 404 | Not found (e.g. a file that does not exist or is not owned by the caller) |
| 409 | Conflict — email is already registered |

## JSON-RPC (over WebSocket `/ws/v1`)

Errors follow the JSON-RPC 2.0 error object format with a custom `code` field.

| Code | Meaning |
| --- | --- |
| 4004 | Not found (e.g. a board or file that does not exist or is not owned by the caller) |
| 4022 | Validation error (e.g. empty title, or `files.v1.browse` with an invalid path) |
| -32000 | Unexpected server error — StreamJsonRpc's default invocation-error code. Any exception that is not a `LocalRpcException` (e.g. an unexpected SQL error) surfaces here, with the message and stack in `error.data`. No custom `5000` code is emitted today. |

> In Development builds, the `system.v1.throw` method deliberately throws an unhandled
> exception so you can inspect this `-32000` response. It is not registered in production.

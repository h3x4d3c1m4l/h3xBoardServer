# Error codes

## REST endpoints (`/api/v1/auth/*`, `/api/v1/files`, `/api/v1/view/*`)

Errors are returned as `application/problem+json` (RFC 7807).

| HTTP status | Meaning |
| --- | --- |
| 400 | Validation error (e.g. password too short; a file upload with an invalid path/filename, empty body, or over `Storage:MaxUploadBytes`) |
| 401 | Invalid credentials or session not found |
| 404 | Not found (e.g. a file that does not exist or is not owned by the caller; a view file whose share code is unknown or whose id is not in the session's snapshot `fileIds`) |
| 409 | Conflict — email is already registered |
| 429 | Too many attempts — the anonymous viewer WebSocket rate-limits share-code lookups per IP (plain-text body, not problem+json, since it is rejected before the WebSocket upgrade) |

## JSON-RPC (over WebSocket `/ws/v1`)

Errors follow the JSON-RPC 2.0 error object format with a custom `code` field. Codes are aligned
with the HTTP status of the same meaning (4004 ↔ 404, 4009 ↔ 409, 4013 ↔ 413, 4022 ↔ 422).

| Code | Meaning |
| --- | --- |
| 4004 | Not found (e.g. a board or file that does not exist or is not owned by the caller; a `sharing.v1.*` call without an active session, or after it expired) |
| 4009 | Conflict (e.g. `sharing.v1.start` could not claim a share code after several attempts — should effectively never happen) |
| 4013 | Payload too large (e.g. a `sharing.v1.publish` batch over `Sharing:MaxMessageBytes`) |
| 4022 | Validation error (e.g. empty title, `files.v1.browse` with an invalid path, or a `sharing.v1.publish` envelope missing `type`/`seq`) |
| -32000 | Unexpected server error — StreamJsonRpc's default invocation-error code. Any exception that is not a `LocalRpcException` (e.g. an unexpected SQL error) surfaces here, with the message and stack in `error.data`. No custom `5000` code is emitted today. |

> In Development builds, the `system.v1.throw` method deliberately throws an unhandled
> exception so you can inspect this `-32000` response. It is not registered in production.

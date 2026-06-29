# File storage

The server stores binary files (the first use case is custom board backgrounds) behind a pluggable
storage abstraction. **Metadata** lives in the database; the **bytes** live in a storage backend.

## Two layers

| Concern | Where | Why |
| --- | --- | --- |
| Metadata (owner, path, name, size, …) | `files` table (linq2db + FluentMigrator) | Source of truth for *browse* and access control — browsing never depends on a backend's listing API |
| Bytes | [`IFileStorage`](../Storage/IFileStorage.cs) backend | Swappable: file system today, S3/Azure later |

```
files.v1.*  →  FilesRpcV1  →  FileService  →  H3xBoardDb (files table)  +  IFileStorage
                                                                            └─ FileSystemFileStorage
```

## Owner-scoped layout

A file belongs to an owner expressed as `(ownerScope, ownerId)`. The storage key — the path the bytes
live under — is **server-generated** from a UUID, so no client input ever touches the path (no
traversal, no collisions):

```
{pluralScope}/{ownerId}/{fileId}
```

- `ownerScope = "user"`  → `users/{userId}/{fileId}`   *(built today)*
- `ownerScope = "company"` → `companies/{companyId}/{fileId}`   *(future — add the scope value and an
  authorization rule; no schema change needed, the `owner_scope` column already exists)*

### Virtual folders (path)

The **physical** storage key above is flat and UUID-based. On top of it the row carries a **virtual**
location that is what clients actually see and browse:

- `path` — the folder, forward-slash separated, `""` = root (e.g. `boards/123/backgrounds`).
- `file_name` — the leaf within that folder (e.g. `sunset.jpg`).

Together they are the file's logical address; the file's physical bytes still live at the flat
`users/{userId}/{fileId}` key. Because `path`/`file_name` are **decoupled from the storage key**,
moving or renaming a file is a metadata-only update — no bytes move, and it behaves identically on
S3/Azure. `path` is client-supplied, so the server normalizes and validates it: forward slashes only,
no empty / `.` / `..` segments, no leading/trailing slash; `file_name` may not contain a separator.

`files.v1.browse` lists one folder at a time — the files directly in `path` plus the immediate
sub-folder names under it — so a client can render a real folder tree. (Two files with the same
`path` + `file_name` are allowed today; uniqueness is not enforced.)

Board association is intentionally *not* modelled server-side for ordinary uploads: the Flutter client
stores the returned `fileId` inside the board's opaque `data` blob. The one exception is **board
screenshots** (below), which are a dedicated, server-managed file kind.

## File kinds & board screenshots

Every row carries a `kind` ([`FileKind`](../Data/Entities/FileKind.cs), stored as a readable string):

- `user` — an ordinary upload. The **only** kind that appears in `files.v1.browse` and the only kind
  `files.v1.delete` can address.
- `board-screenshot` — a per-board screenshot, **hidden** from the generic file API.

Browse and delete both filter to `kind = user`, so system files never leak into the user's own file
listing and can't be deleted (or even named) through the generic endpoints.

### Board screenshots

The Flutter client renders a screenshot of a board (first sub-board today) and periodically uploads it,
**independently of the board data save cycle**. The server stores it as a `board-screenshot` file linked
1:1 from `boards.screenshot_file_id`:

- Upload is an **upsert**: the first call creates the file and links it; later calls **overwrite the
  bytes in place** (same id and storage key), so the periodic re-uploads never pile up orphans.
- Setting a screenshot deliberately does **not** touch the board's `updated_at`, so it doesn't reorder
  `boards.v1.list` ("most recently updated").
- `BoardSummary`/`BoardDto` expose `hasScreenshot` so a client knows whether to fetch a thumbnail.
- Deleting a board cascade-deletes its screenshot (bytes + row).

## Configuration

```jsonc
"Storage": {
  "Backend": "FileSystem",        // selects the IFileStorage implementation
  "MaxUploadBytes": 10485760,     // 10 MiB upload cap (also advertised via /api/v1/server/info)
  "FileSystem": {
    "RootPath": "storage-data"    // root directory for the file-system backend
                                  // (not "storage" — collides with the Storage/ source dir on
                                  // case-insensitive filesystems)
  }
}
```

In Docker the root is `/data/files` (set in the `Dockerfile`), so files persist in the same `/data`
volume as the SQLite database.

## Transport

The API is split by payload type:

| Operation | Transport | Why |
| --- | --- | --- |
| browse, delete | WebSocket JSON-RPC (`files.v1.browse` / `files.v1.delete`) | Metadata only — see [json-rpc-methods.md](json-rpc-methods.md) |
| upload, download | REST (`POST` / `GET /api/v1/files`) | Binary streams natively over HTTP — no base64 bloat, no whole-file buffering |
| board screenshot upload/download | REST (`PUT` / `GET /api/v1/boards/{id}/screenshot`) | Same binary-over-HTTP rationale; upsert keyed by board |

Both transports share the same `.h3xboard.session` cookie for auth.

### REST endpoints

**`POST /api/v1/files`** — `multipart/form-data` with a `file` part and an optional `path` form field
(the destination virtual folder, `""`/omitted = root). The leaf name and content type come from the
`file` part. Returns `201 Created` with the file metadata (same shape as a browse entry). Rejected with
`400` for an invalid path/filename, an empty file, or a file over `Storage:MaxUploadBytes`.

```sh
curl -b cookies.txt -F "path=boards/123/backgrounds" -F "file=@sunset.jpg" \
     http://localhost:8081/api/v1/files
```

**`GET /api/v1/files/{id}`** — streams the bytes with the original `Content-Type` and filename
(`Content-Disposition`). `404` if the file does not exist or is not owned by the caller.

```sh
curl -b cookies.txt -OJ http://localhost:8081/api/v1/files/{id}
```

Both require a valid session (`401` otherwise). `IFileStorage` is stream-based, so uploads stream
straight to the backend and downloads stream straight from it.

#### Board screenshot endpoints

**`PUT /api/v1/boards/{boardId}/screenshot`** — `multipart/form-data` with a `file` part (the screenshot
image). Upserts: replaces any existing screenshot for the board. Returns `200 OK` with the screenshot
file's metadata. `404` if the board isn't owned by the caller; `400` for an empty/oversized file.

```sh
curl -b cookies.txt -X PUT -F "file=@board.png" \
     http://localhost:8081/api/v1/boards/{boardId}/screenshot
```

**`GET /api/v1/boards/{boardId}/screenshot`** — streams the screenshot bytes. `404` if the board has no
screenshot (or isn't owned by the caller).

## Adding a storage backend

Parallels [adding-a-database-provider.md](adding-a-database-provider.md):

1. Implement [`IFileStorage`](../Storage/IFileStorage.cs) (e.g. `S3FileStorage`) — only write / read /
   delete / exists on an opaque key.
2. Add the SDK NuGet package(s).
3. Register it in `Program.cs` — switch on `Storage:Backend` and bind `IFileStorage` to the chosen
   implementation.

No other code changes: `FileService` and `FilesRpcV1` are backend-agnostic.

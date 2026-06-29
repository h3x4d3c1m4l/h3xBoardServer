using H3xBoardServer.Storage;

namespace H3xBoardServer.Services;

/// <summary>
/// File business logic. Metadata lives in the <c>files</c> table (source of truth for browsing
/// and access control); the bytes live in an <see cref="IFileStorage"/> backend. Every query is
/// scoped to the owning user so files cannot leak across accounts.
/// </summary>
public class FileService(H3xBoardDbFactory dbFactory, IFileStorage storage, IConfiguration configuration)
{
    // Only "user"-owned files exist today; a future "company" scope reuses the same table/columns.
    private const string OwnerScopeUser = "user";
    private const long DefaultMaxUploadBytes = 10L * 1024 * 1024;

    public async Task<BrowseFilesResult> BrowseAsync(BrowseFilesRequest request, string userId)
    {
        var prefix = NormalizePath(request.Path);

        await using var db = dbFactory.Create();
        // Only the owner's own uploads are browsable — system files (e.g. board screenshots) are hidden.
        var query = db.Files.Where(f => f.OwnerScope == OwnerScopeUser && f.OwnerId == userId && f.Kind == FileKind.User);

        // Restrict to the folder subtree (everything at or under the prefix). Root ("") matches all.
        if (prefix.Length > 0)
        {
            var subtree = prefix + "/";
            query = query.Where(f => f.Path == prefix || f.Path.StartsWith(subtree));
        }

        var rows = await query.OrderByDescending(f => f.CreatedAt).ToListAsync();

        // Files directly in this folder; sub-folders are the distinct first segment of deeper paths.
        var files = new List<FileSummary>();
        var folders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in rows)
        {
            if (f.Path == prefix)
            {
                files.Add(new FileSummary(f.Id, f.Path, f.FileName, f.ContentType, f.SizeBytes, f.CreatedAt, f.UpdatedAt));
            }
            else
            {
                var relative = prefix.Length == 0 ? f.Path : f.Path[(prefix.Length + 1)..];
                var firstSegment = relative.Split('/', 2)[0];
                if (firstSegment.Length > 0)
                    folders.Add(firstSegment);
            }
        }

        return new BrowseFilesResult(prefix, folders.ToList(), files);
    }

    /// <summary>
    /// Stores an uploaded file. The bytes are streamed straight to the backend — no buffering — and
    /// <paramref name="sizeBytes"/> (from the multipart part) is trusted for the size cap. Called from
    /// the REST upload endpoint; see <c>docs/file-storage.md</c>.
    /// </summary>
    public async Task<FileSummary> UploadAsync(Stream content, long sizeBytes, string fileName, string contentType, string? path, string userId)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedName = NormalizeFileName(fileName);

        if (string.IsNullOrWhiteSpace(contentType))
            throw RpcErrors.Validation("ContentType is required");

        var maxBytes = configuration.GetValue("Storage:MaxUploadBytes", DefaultMaxUploadBytes);
        if (sizeBytes <= 0)
            throw RpcErrors.Validation("File is empty");
        if (sizeBytes > maxBytes)
            throw RpcErrors.Validation($"File exceeds the maximum upload size of {maxBytes} bytes");

        var now = DateTime.UtcNow;
        var fileId = Guid.NewGuid().ToString();
        // Server-generated key — no user input touches the path (no traversal / collisions).
        var storageKey = $"users/{userId}/{fileId}";

        await storage.WriteAsync(storageKey, content);

        var entity = new FileEntity
        {
            Id = fileId,
            OwnerScope = OwnerScopeUser,
            OwnerId = userId,
            StorageKey = storageKey,
            Path = normalizedPath,
            FileName = normalizedName,
            Kind = FileKind.User,
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var db = dbFactory.Create();
        await db.InsertAsync(entity);

        return new FileSummary(entity.Id, entity.Path, entity.FileName, entity.ContentType, entity.SizeBytes, entity.CreatedAt, entity.UpdatedAt);
    }

    /// <summary>
    /// Opens an owner's file for download — returns its metadata plus an open stream of the bytes.
    /// The caller owns (and must dispose) <see cref="FileContent.Content"/>. Called from the REST
    /// download endpoint.
    /// </summary>
    public async Task<FileContent> OpenDownloadAsync(string id, string userId)
    {
        await using var db = dbFactory.Create();
        var entity = await db.Files
            .Where(f => f.Id == id && f.OwnerScope == OwnerScopeUser && f.OwnerId == userId)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("File not found");

        var stream = await storage.ReadAsync(entity.StorageKey);
        return new FileContent(stream, entity.FileName, entity.ContentType, entity.SizeBytes);
    }

    public async Task DeleteAsync(string id, string userId)
    {
        await using var db = dbFactory.Create();
        // System files (e.g. board screenshots) are not addressable here — only the owner's uploads.
        var entity = await db.Files
            .Where(f => f.Id == id && f.OwnerScope == OwnerScopeUser && f.OwnerId == userId && f.Kind == FileKind.User)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("File not found");

        // Delete bytes first: a leftover row pointing at missing bytes is worse than an orphan
        // blob (which can be garbage-collected later).
        await storage.DeleteAsync(entity.StorageKey);
        await db.Files.Where(f => f.Id == id).DeleteAsync();
    }

    // //////////////// //
    // Board screenshots //
    // //////////////// //

    /// <summary>
    /// Upserts the screenshot for a board (verifying the board belongs to <paramref name="userId"/>).
    /// The screenshot is a hidden <see cref="FileKind.BoardScreenshot"/> file linked 1:1 from
    /// <see cref="BoardEntity.ScreenshotFileId"/>. If one already exists its bytes are overwritten in
    /// place (same id and storage key) so the periodic re-uploads never accumulate orphans. Setting a
    /// screenshot deliberately does <b>not</b> bump the board's <c>UpdatedAt</c>, so it won't reorder
    /// <c>boards.v1.list</c>. Called from the REST screenshot endpoint.
    /// </summary>
    public async Task<FileSummary> SetBoardScreenshotAsync(string boardId, Stream content, long sizeBytes, string contentType, string userId)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            throw RpcErrors.Validation("ContentType is required");

        var maxBytes = configuration.GetValue("Storage:MaxUploadBytes", DefaultMaxUploadBytes);
        if (sizeBytes <= 0)
            throw RpcErrors.Validation("File is empty");
        if (sizeBytes > maxBytes)
            throw RpcErrors.Validation($"File exceeds the maximum upload size of {maxBytes} bytes");

        await using var db = dbFactory.Create();
        var board = await db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("Board not found");

        var now = DateTime.UtcNow;

        // Overwrite the existing screenshot in place when present, keeping its id + storage key stable.
        if (board.ScreenshotFileId is not null)
        {
            var existing = await db.Files
                .Where(f => f.Id == board.ScreenshotFileId && f.OwnerScope == OwnerScopeUser && f.OwnerId == userId && f.Kind == FileKind.BoardScreenshot)
                .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();
            if (existing is not null)
            {
                await storage.WriteAsync(existing.StorageKey, content);
                existing.ContentType = contentType.Trim();
                existing.SizeBytes = sizeBytes;
                existing.UpdatedAt = now;
                await db.UpdateAsync(existing);
                return new FileSummary(existing.Id, existing.Path, existing.FileName, existing.ContentType, existing.SizeBytes, existing.CreatedAt, existing.UpdatedAt);
            }
            // Link was dangling (bytes/row gone) — fall through and recreate it below.
        }

        var fileId = Guid.NewGuid().ToString();
        var storageKey = $"users/{userId}/{fileId}";
        await storage.WriteAsync(storageKey, content);

        var entity = new FileEntity
        {
            Id = fileId,
            OwnerScope = OwnerScopeUser,
            OwnerId = userId,
            StorageKey = storageKey,
            Path = "",
            FileName = "screenshot",
            Kind = FileKind.BoardScreenshot,
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await db.InsertAsync(entity);
        // Link the board to its screenshot without touching UpdatedAt (a targeted column update).
        await db.Boards.Where(b => b.Id == boardId).Set(b => b.ScreenshotFileId, fileId).UpdateAsync();

        return new FileSummary(entity.Id, entity.Path, entity.FileName, entity.ContentType, entity.SizeBytes, entity.CreatedAt, entity.UpdatedAt);
    }

    /// <summary>
    /// Opens a board's screenshot for download (verifying board ownership). Throws
    /// <see cref="RpcErrors.NotFound"/> if the board is unknown or has no screenshot yet.
    /// </summary>
    public async Task<FileContent> OpenBoardScreenshotAsync(string boardId, string userId)
    {
        await using var db = dbFactory.Create();
        var board = await db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("Board not found");

        if (board.ScreenshotFileId is null)
            throw RpcErrors.NotFound("Board has no screenshot");

        var entity = await db.Files
            .Where(f => f.Id == board.ScreenshotFileId && f.OwnerScope == OwnerScopeUser && f.OwnerId == userId && f.Kind == FileKind.BoardScreenshot)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("Board has no screenshot");

        var stream = await storage.ReadAsync(entity.StorageKey);
        return new FileContent(stream, entity.FileName, entity.ContentType, entity.SizeBytes);
    }

    /// <summary>
    /// Deletes a board-screenshot file (bytes + row) by id. A no-op if it is already gone or is not a
    /// screenshot owned by <paramref name="userId"/>. Used to cascade on board deletion.
    /// </summary>
    public async Task DeleteScreenshotAsync(string fileId, string userId)
    {
        await using var db = dbFactory.Create();
        var entity = await db.Files
            .Where(f => f.Id == fileId && f.OwnerScope == OwnerScopeUser && f.OwnerId == userId && f.Kind == FileKind.BoardScreenshot)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();
        if (entity is null)
            return;

        await storage.DeleteAsync(entity.StorageKey);
        await db.Files.Where(f => f.Id == fileId).DeleteAsync();
    }

    /// <summary>
    /// Normalizes a virtual folder path: forward-slash separated, no empty/"."/".." segments, no
    /// leading/trailing slash. null/blank → "" (root). This is metadata only (it never touches the
    /// file system), but it is client-supplied so it is validated and canonicalized.
    /// </summary>
    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var clean = new List<string>(segments.Length);
        foreach (var raw in segments)
        {
            var segment = raw.Trim();
            if (segment.Length == 0)
                continue;
            if (segment is "." or "..")
                throw RpcErrors.Validation("Path may not contain '.' or '..' segments");
            clean.Add(segment);
        }

        return string.Join('/', clean);
    }

    /// <summary>Validates a leaf file name — required, no path separators, not "."/"..".</summary>
    private static string NormalizeFileName(string? fileName)
    {
        var name = fileName?.Trim() ?? "";
        if (name.Length == 0)
            throw RpcErrors.Validation("FileName is required");
        if (name.Contains('/') || name.Contains('\\'))
            throw RpcErrors.Validation("FileName may not contain path separators — use Path for folders");
        if (name is "." or "..")
            throw RpcErrors.Validation("FileName is invalid");
        return name;
    }
}

/// <summary>
/// A file opened for download: its bytes plus the metadata needed to serve them. Not a wire DTO —
/// the REST endpoint streams <see cref="Content"/> and disposes it.
/// </summary>
public record FileContent(Stream Content, string FileName, string ContentType, long SizeBytes);

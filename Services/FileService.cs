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
        var query = db.Files.Where(f => f.OwnerScope == OwnerScopeUser && f.OwnerId == userId);

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
        var entity = await db.Files
            .Where(f => f.Id == id && f.OwnerScope == OwnerScopeUser && f.OwnerId == userId)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("File not found");

        // Delete bytes first: a leftover row pointing at missing bytes is worse than an orphan
        // blob (which can be garbage-collected later).
        await storage.DeleteAsync(entity.StorageKey);
        await db.Files.Where(f => f.Id == id).DeleteAsync();
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

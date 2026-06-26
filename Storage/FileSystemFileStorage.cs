namespace H3xBoardServer.Storage;

/// <summary>
/// <see cref="IFileStorage"/> backed by the local file system. Bytes live under
/// <c>Storage:FileSystem:RootPath</c> (default <c>storage</c>; <c>/data/files</c> in Docker). The
/// storage key is server-generated and slash-separated (<c>users/{userId}/{fileId}</c>); it maps
/// directly onto a path under the root, with the leading directories created on write.
/// </summary>
public class FileSystemFileStorage : IFileStorage
{
    private const string DefaultRootPath = "storage";

    private readonly string _root;

    public FileSystemFileStorage(IConfiguration configuration)
    {
        var configured = configuration.GetValue("Storage:FileSystem:RootPath", DefaultRootPath)!;
        _root = Path.GetFullPath(configured);
    }

    public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(key);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Stored file not found", key);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(key);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(ResolvePath(key)));

    /// <summary>
    /// Maps an opaque storage key onto an absolute path under the root, guarding against escaping the
    /// root. Keys are server-generated today, but this stays defensive in case that ever changes.
    /// </summary>
    private string ResolvePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required", nameof(key));

        var relative = key.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_root, relative));

        var rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
            throw new ArgumentException("Storage key resolves outside the storage root", nameof(key));

        return fullPath;
    }
}

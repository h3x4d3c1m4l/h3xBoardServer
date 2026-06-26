namespace H3xBoardServer.Storage;

/// <summary>
/// Pluggable backend for the <b>bytes</b> of a stored file, keyed by an opaque, server-generated
/// storage key (see <c>docs/file-storage.md</c>). Metadata (owner, virtual path, name, size) lives in
/// the <c>files</c> table — the backend knows nothing about it and is never used for browsing or
/// access control. The file-system implementation is built today; S3/Azure are drop-in via the
/// <c>Storage:Backend</c> switch in <c>Program.cs</c>.
/// </summary>
public interface IFileStorage
{
    /// <summary>Writes (or overwrites) the bytes at <paramref name="key"/>, streamed from <paramref name="content"/>.</summary>
    Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Opens the bytes at <paramref name="key"/> for reading. The caller owns and disposes the returned stream.</summary>
    Task<Stream> ReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes the bytes at <paramref name="key"/>. A no-op if nothing is stored there.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>True if bytes are stored at <paramref name="key"/>.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}

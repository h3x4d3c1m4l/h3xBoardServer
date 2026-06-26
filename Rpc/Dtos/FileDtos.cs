namespace H3xBoardServer.Rpc.Dtos;

/// <summary>
/// Metadata for a stored file — no bytes. Returned by browse and upload.
/// <c>Path</c> is the virtual folder the file lives in (forward-slash separated, "" = root);
/// <c>FileName</c> is the leaf. Together they form the file's logical address within the owner.
/// </summary>
public record FileSummary(
    string Id,
    string Path,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Browse request. The owner is always the authenticated caller. <c>Path</c> is the folder to list
/// (null/"" = root); browse returns the files directly in that folder plus its immediate sub-folders.
/// </summary>
public record BrowseFilesRequest(
    string? Path = null);

/// <summary>
/// The contents of one virtual folder: the immediate sub-folder names and the files directly in it.
/// </summary>
public record BrowseFilesResult(
    string Path,
    IReadOnlyList<string> Folders,
    IReadOnlyList<FileSummary> Files);

using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

/// <summary>
/// JSON-RPC methods for file storage — v1. Covers metadata operations only (browse, delete); the
/// file bytes are uploaded/downloaded over REST (<c>POST</c>/<c>GET /api/v1/files</c>) since binary
/// streams over plain HTTP rather than base64-over-WebSocket. See <c>docs/file-storage.md</c>.
/// All methods require authentication and operate on the authenticated user's files.
/// </summary>
public class FilesRpcV1(FileService fileService, RpcContext context)
{
    /// <summary>
    /// Lists one virtual folder: its immediate sub-folders and the files directly in it (metadata
    /// only, no bytes; files newest first). Pass a path to descend; null/"" lists the root.
    /// </summary>
    [JsonRpcMethod("files.v1.browse")]
    public Task<BrowseFilesResult> Browse(BrowseFilesRequest request)
    {
        return fileService.BrowseAsync(request, context.UserId!);
    }

    /// <summary>
    /// Permanently deletes a file (bytes and metadata). There is no undo.
    /// </summary>
    [JsonRpcMethod("files.v1.delete")]
    public Task Delete(string id)
    {
        return fileService.DeleteAsync(id, context.UserId!);
    }
}

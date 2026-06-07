using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

/// <summary>
/// JSON-RPC methods for board CRUD — v1. All methods require authentication.
/// </summary>
public class BoardsRpcV1(BoardService boardService, RpcContext context)
{
    /// <summary>
    /// Returns lightweight summaries of all boards owned by the authenticated user,
    /// ordered by most recently updated.
    /// </summary>
    [JsonRpcMethod("boards.v1.list")]
    public Task<List<BoardSummary>> List()
    {
        context.RequireAuthentication();
        return boardService.GetBoardsForUserAsync(context.UserId!);
    }

    /// <summary>
    /// Returns a single board including its full data blob.
    /// </summary>
    [JsonRpcMethod("boards.v1.get")]
    public Task<BoardDto> Get(string id)
    {
        context.RequireAuthentication();
        return boardService.GetBoardAsync(id, context.UserId!);
    }

    /// <summary>
    /// Creates a new board. Pass an empty data object {} if no state yet.
    /// </summary>
    [JsonRpcMethod("boards.v1.create")]
    public Task<BoardDto> Create(CreateBoardRequest request)
    {
        context.RequireAuthentication();
        return boardService.CreateBoardAsync(request, context.UserId!);
    }

    /// <summary>
    /// Partial update — only fields present in the request are changed.
    /// Send the full board data blob when saving drawing state.
    /// </summary>
    [JsonRpcMethod("boards.v1.update")]
    public Task<BoardDto> Update(UpdateBoardRequest request)
    {
        context.RequireAuthentication();
        return boardService.UpdateBoardAsync(request, context.UserId!);
    }

    /// <summary>
    /// Permanently deletes a board. There is no soft-delete or undo.
    /// </summary>
    [JsonRpcMethod("boards.v1.delete")]
    public Task Delete(string id)
    {
        context.RequireAuthentication();
        return boardService.DeleteBoardAsync(id, context.UserId!);
    }
}

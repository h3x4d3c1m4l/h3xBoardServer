namespace H3xBoardServer.Services;

public class BoardService(H3xBoardDbFactory dbFactory)
{
    public async Task<List<BoardSummary>> GetBoardsForUserAsync(int userId)
    {
        await using var db = dbFactory.Create();
        return await db.Boards
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.UpdatedAt)
            .Select(b => new BoardSummary(b.Id, b.Title, b.CreatedAt, b.UpdatedAt))
            .ToListAsync();
    }

    public async Task<BoardDto> GetBoardAsync(string id, int userId)
    {
        await using var db = dbFactory.Create();
        var entity = await db.Boards
            .Where(b => b.Id == id && b.UserId == userId)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("Board not found");

        return MapToDto(entity);
    }

    public async Task<BoardDto> CreateBoardAsync(CreateBoardRequest request, int userId)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw RpcErrors.Validation("Title is required");

        var now = DateTime.UtcNow;
        var entity = new BoardEntity
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Title = request.Title.Trim(),
            Data = request.Data?.GetRawText() ?? "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var db = dbFactory.Create();
        await db.InsertAsync(entity);

        return MapToDto(entity);
    }

    public async Task<BoardDto> UpdateBoardAsync(UpdateBoardRequest request, int userId)
    {
        await using var db = dbFactory.Create();
        var entity = await db.Boards
            .Where(b => b.Id == request.Id && b.UserId == userId)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync()
            ?? throw RpcErrors.NotFound("Board not found");

        if (request.Title != null)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                throw RpcErrors.Validation("Title cannot be empty");
            entity.Title = request.Title.Trim();
        }

        if (request.Data.HasValue)
            entity.Data = request.Data.Value.GetRawText();

        entity.UpdatedAt = DateTime.UtcNow;
        await db.UpdateAsync(entity);

        return MapToDto(entity);
    }

    public async Task DeleteBoardAsync(string id, int userId)
    {
        await using var db = dbFactory.Create();
        var deleted = await db.Boards
            .Where(b => b.Id == id && b.UserId == userId)
            .DeleteAsync();

        if (deleted == 0)
            throw RpcErrors.NotFound("Board not found");
    }

    private static BoardDto MapToDto(BoardEntity entity)
    {
        // Clone so the JsonElement owns its memory independent of the parsed document
        var data = JsonDocument.Parse(entity.Data).RootElement.Clone();
        return new BoardDto(entity.Id, entity.Title, data, entity.CreatedAt, entity.UpdatedAt);
    }
}

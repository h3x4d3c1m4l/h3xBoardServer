namespace H3xBoardServer.Rpc.Dtos;

/// <summary>
/// Lightweight summary for board list responses — no data blob. <c>HasScreenshot</c> tells the client
/// whether <c>GET /api/v1/boards/{id}/screenshot</c> will return an image (for thumbnails).
/// </summary>
public record BoardSummary(
    string Id,
    string Title,
    bool HasScreenshot,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Full board including the opaque JSON data blob owned by the Flutter client.
/// The data field contains everything: background, line settings, widgets, drawing strokes.
/// <c>HasScreenshot</c> tells the client whether a screenshot is available to fetch over REST.
/// </summary>
public record BoardDto(
    string Id,
    string Title,
    JsonElement Data,
    bool HasScreenshot,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateBoardRequest(
    string Title,
    JsonElement? Data = null);

public record UpdateBoardRequest(
    string Id,
    string? Title = null,
    JsonElement? Data = null);

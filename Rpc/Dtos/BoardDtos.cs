namespace H3xBoardServer.Rpc.Dtos;

/// <summary>
/// Lightweight summary for board list responses — no data blob.
/// </summary>
public record BoardSummary(
    string Id,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Full board including the opaque JSON data blob owned by the Flutter client.
/// The data field contains everything: background, line settings, widgets, drawing strokes.
/// </summary>
public record BoardDto(
    string Id,
    string Title,
    JsonElement Data,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateBoardRequest(
    string Title,
    JsonElement? Data = null);

public record UpdateBoardRequest(
    string Id,
    string? Title = null,
    JsonElement? Data = null);

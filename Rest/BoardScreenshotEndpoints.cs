using StreamJsonRpc;

namespace H3xBoardServer.Rest;

/// <summary>
/// REST endpoints for board <b>screenshot</b> bytes. Like the generic file endpoints, the bytes ride
/// REST (binary streams over plain HTTP) while the rest of the board API is JSON-RPC over the
/// WebSocket. A board has at most one screenshot — uploading replaces it. Screenshots are hidden
/// <see cref="H3xBoardServer.Data.Entities.FileKind.BoardScreenshot"/> files: they never appear in
/// <c>files.v1.browse</c> and cannot be deleted via <c>files.v1.delete</c>. Both endpoints require the
/// same session cookie as the rest of the API. See <c>docs/file-storage.md</c>.
/// </summary>
public static class BoardScreenshotEndpoints
{
    public static IEndpointRouteBuilder MapBoardScreenshotEndpoints(this IEndpointRouteBuilder app)
    {
        // Upsert — multipart/form-data with a "file" part (the screenshot image). Replaces any existing
        // screenshot for the board. Returns the screenshot file's metadata.
        app.MapPut("/api/v1/boards/{boardId}/screenshot", async (string boardId, HttpContext httpContext, FileService fileService) =>
        {
            await httpContext.Session.LoadAsync();
            var userId = httpContext.Session.GetString("userId");
            if (userId is null)
                return Results.Unauthorized();

            if (!httpContext.Request.HasFormContentType)
                return Results.Problem("Expected multipart/form-data with a 'file' part", statusCode: StatusCodes.Status400BadRequest);

            var form = await httpContext.Request.ReadFormAsync();
            var file = form.Files["file"];
            if (file is null)
                return Results.Problem("Missing 'file' part", statusCode: StatusCodes.Status400BadRequest);

            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

            try
            {
                await using var stream = file.OpenReadStream();
                var summary = await fileService.SetBoardScreenshotAsync(boardId, stream, file.Length, contentType, userId);
                return Results.Ok(summary);
            }
            catch (LocalRpcException ex)
            {
                return Results.Problem(ex.Message, statusCode: FileEndpoints.MapStatus(ex.ErrorCode));
            }
        });

        // Download — streams the screenshot bytes with their content type. 404 if the board has none.
        app.MapGet("/api/v1/boards/{boardId}/screenshot", async (string boardId, HttpContext httpContext, FileService fileService) =>
        {
            await httpContext.Session.LoadAsync();
            var userId = httpContext.Session.GetString("userId");
            if (userId is null)
                return Results.Unauthorized();

            try
            {
                var content = await fileService.OpenBoardScreenshotAsync(boardId, userId);
                // Results.File streams and disposes the stream once the response is written.
                return Results.File(content.Content, content.ContentType, content.FileName);
            }
            catch (LocalRpcException ex)
            {
                return Results.Problem(ex.Message, statusCode: FileEndpoints.MapStatus(ex.ErrorCode));
            }
        });

        return app;
    }
}

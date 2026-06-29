using StreamJsonRpc;

namespace H3xBoardServer.Rest;

/// <summary>
/// REST endpoints for file bytes. Browsing and deletion live on the WebSocket JSON-RPC API
/// (<c>files.v1.*</c>); upload/download are REST so binary streams over plain HTTP instead of
/// base64-over-WebSocket. Both require the same session cookie as the rest of the API.
/// See <c>docs/file-storage.md</c>.
/// </summary>
public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // Upload — multipart/form-data: a "file" part, plus an optional "path" form field (the
        // destination virtual folder, "" = root). Returns the new file's metadata.
        app.MapPost("/api/v1/files", async (HttpContext httpContext, FileService fileService) =>
        {
            var userId = httpContext.Session.GetString("userId");
            if (userId is null)
                return Results.Unauthorized();

            if (!httpContext.Request.HasFormContentType)
                return Results.Problem("Expected multipart/form-data with a 'file' part", statusCode: StatusCodes.Status400BadRequest);

            var form = await httpContext.Request.ReadFormAsync();
            var file = form.Files["file"];
            if (file is null)
                return Results.Problem("Missing 'file' part", statusCode: StatusCodes.Status400BadRequest);

            var path = form["path"].ToString();
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

            try
            {
                await using var stream = file.OpenReadStream();
                var summary = await fileService.UploadAsync(stream, file.Length, file.FileName, contentType, path, userId);
                return Results.Created($"/api/v1/files/{summary.Id}", summary);
            }
            catch (LocalRpcException ex)
            {
                return Results.Problem(ex.Message, statusCode: MapStatus(ex.ErrorCode));
            }
        });

        // Download — streams the bytes with the original filename and content type.
        app.MapGet("/api/v1/files/{id}", async (string id, HttpContext httpContext, FileService fileService) =>
        {
            var userId = httpContext.Session.GetString("userId");
            if (userId is null)
                return Results.Unauthorized();

            try
            {
                var content = await fileService.OpenDownloadAsync(id, userId);
                // Results.File streams and disposes the stream once the response is written.
                return Results.File(content.Content, content.ContentType, content.FileName);
            }
            catch (LocalRpcException ex)
            {
                return Results.Problem(ex.Message, statusCode: MapStatus(ex.ErrorCode));
            }
        });

        return app;
    }

    // FileService throws LocalRpcException with HTTP-status-aligned RpcErrors codes (shared with the
    // WebSocket surface); map them back to HTTP status codes for the REST response.
    internal static int MapStatus(int errorCode) => errorCode switch
    {
        RpcErrors.CodeNotFound => StatusCodes.Status404NotFound,
        RpcErrors.CodeValidation => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };
}

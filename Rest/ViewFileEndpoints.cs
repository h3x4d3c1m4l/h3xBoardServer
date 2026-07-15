using H3xBoardServer.Services.Sharing;
using StreamJsonRpc;

namespace H3xBoardServer.Rest;

/// <summary>
/// REST endpoint for share-session file bytes (board backgrounds referenced by the presenter's
/// snapshot). Anonymous — the share code is the credential — but strictly scoped: only file ids the
/// presenter listed in its latest snapshot's <c>fileIds</c> are downloadable, streamed via the same
/// owner-scoped <see cref="FileService"/> path as <c>GET /api/v1/files/{id}</c> on behalf of the
/// presenter. Everything else is a plain 404 (no distinction between unknown code and unlisted
/// file, so the endpoint leaks nothing). See <c>docs/live-sharing.md</c>.
/// </summary>
public static class ViewFileEndpoints
{
    public static IEndpointRouteBuilder MapViewFileEndpoints(this IEndpointRouteBuilder app)
    {
        // Download — streams the bytes with the original filename and content type. Snapshots
        // reference immutable uploads, so viewers may cache aggressively (but privately).
        app.MapGet("/api/v1/view/{code}/files/{fileId}", async (string code, string fileId, HttpContext httpContext, IShareStore store, FileService fileService) =>
        {
            var normalizedCode = ShareCodes.Normalize(code);
            var session = ShareCodes.IsValid(normalizedCode) ? await store.GetSessionAsync(normalizedCode) : null;
            if (session is null || !session.FileIds.Contains(fileId))
                return Results.Problem("File not found", statusCode: StatusCodes.Status404NotFound);

            try
            {
                var content = await fileService.OpenDownloadAsync(fileId, session.PresenterUserId);
                httpContext.Response.Headers.CacheControl = "private, max-age=3600";
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

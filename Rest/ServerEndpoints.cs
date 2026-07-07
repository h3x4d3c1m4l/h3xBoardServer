namespace H3xBoardServer.Rest;

public static class ServerEndpoints
{
    public static IEndpointRouteBuilder MapServerEndpoints(this IEndpointRouteBuilder app)
    {
        // Unauthenticated: lets clients discover server capabilities before logging in.
        app.MapGet("/api/v1/server/info", (IConfiguration configuration) =>
        {
            var registrationAllowed = configuration.GetValue("Auth:AllowRegistration", true);
            var maxUploadBytes = configuration.GetValue("Storage:MaxUploadBytes", 10L * 1024 * 1024);
            var warning = configuration["Server:Warning"];
            if (string.IsNullOrWhiteSpace(warning))
                warning = null;
            return Results.Ok(new ServerInfo(registrationAllowed, maxUploadBytes, warning));
        });

        return app;
    }
}

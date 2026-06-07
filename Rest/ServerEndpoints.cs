namespace H3xBoardServer.Rest;

public static class ServerEndpoints
{
    public static IEndpointRouteBuilder MapServerEndpoints(this IEndpointRouteBuilder app)
    {
        // Unauthenticated: lets clients discover server capabilities before logging in.
        app.MapGet("/api/v1/server/info", (IConfiguration configuration) =>
        {
            var registrationAllowed = configuration.GetValue("Auth:AllowRegistration", true);
            return Results.Ok(new ServerInfo(registrationAllowed));
        });

        return app;
    }
}

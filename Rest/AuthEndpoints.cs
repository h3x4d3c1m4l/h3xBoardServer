namespace H3xBoardServer.Rest;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/register", async (RegisterRequest request, AuthService authService, HttpContext httpContext) =>
        {
            try
            {
                var result = await authService.RegisterAsync(request, httpContext);
                return Results.Created("/api/v1/auth/whoami", result);
            }
            catch (AuthException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.StatusCode);
            }
        });

        app.MapPost("/api/v1/auth/login", async (LoginRequest request, AuthService authService, HttpContext httpContext) =>
        {
            try
            {
                var result = await authService.LoginAsync(request, httpContext);
                return Results.Ok(result);
            }
            catch (AuthException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.StatusCode);
            }
        });

        app.MapPost("/api/v1/auth/logout", (HttpContext httpContext) =>
        {
            httpContext.Session.Clear();
            return Results.NoContent();
        });

        app.MapGet("/api/v1/auth/whoami", (HttpContext httpContext) =>
        {
            var userId = httpContext.Session.GetString("userId");
            var email = httpContext.Session.GetString("email");
            var firstName = httpContext.Session.GetString("firstName");
            var lastName = httpContext.Session.GetString("lastName");
            return userId is null ? Results.Unauthorized() : Results.Ok(new AuthResult(userId, email!, firstName, lastName));
        });

        return app;
    }
}

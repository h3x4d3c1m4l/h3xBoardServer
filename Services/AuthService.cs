namespace H3xBoardServer.Services;

public class AuthException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class AuthService(H3xBoardDbFactory dbFactory, IConfiguration configuration)
{
    public async Task<AuthResult> RegisterAsync(RegisterRequest request, HttpContext httpContext)
    {
        if (!configuration.GetValue("Auth:AllowRegistration", true))
            throw new AuthException(403, "Registration is disabled");

        if (string.IsNullOrWhiteSpace(request.Email))
            throw new AuthException(400, "Email is required");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new AuthException(400, "Password must be at least 8 characters");

        await using var db = dbFactory.Create();

        var exists = await db.Users
            .Where(u => u.Email == request.Email)
            .Take(1).AsAsyncEnumerable().AnyAsync();

        if (exists)
            throw new AuthException(409, "Email is already registered");

        var now = DateTime.UtcNow;
        var user = new UserEntity
        {
            Id = Guid.NewGuid().ToString(),
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await db.InsertAsync(user);

        SetSession(httpContext, user);

        return new AuthResult(user.Id, user.Email, user.FirstName, user.LastName);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            throw new AuthException(401, "Invalid credentials");

        await using var db = dbFactory.Create();

        var user = await db.Users
            .Where(u => u.Email == request.Email)
            .Take(1).AsAsyncEnumerable().FirstOrDefaultAsync();

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new AuthException(401, "Invalid credentials");

        SetSession(httpContext, user);

        return new AuthResult(user.Id, user.Email, user.FirstName, user.LastName);
    }

    private static void SetSession(HttpContext httpContext, UserEntity user)
    {
        httpContext.Session.SetString("userId", user.Id);
        httpContext.Session.SetString("email", user.Email);
        if (user.FirstName is not null)
            httpContext.Session.SetString("firstName", user.FirstName);
        if (user.LastName is not null)
            httpContext.Session.SetString("lastName", user.LastName);
    }
}

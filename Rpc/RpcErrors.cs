using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

public static class RpcErrors
{
    // Error codes in the 4000-5000 range to avoid collision with JSON-RPC reserved codes
    public const int CodeUnauthenticated = 4001;
    public const int CodeInvalidCredentials = 4002;
    public const int CodeNotFound = 4004;
    public const int CodeConflict = 4009;
    public const int CodeValidation = 4022;
    public const int CodeInternal = 5000;

    public static LocalRpcException Unauthenticated(string message = "Authentication required")
        => new(message) { ErrorCode = CodeUnauthenticated };

    public static LocalRpcException InvalidCredentials(string message = "Invalid username or password")
        => new(message) { ErrorCode = CodeInvalidCredentials };

    public static LocalRpcException NotFound(string message = "Not found")
        => new(message) { ErrorCode = CodeNotFound };

    public static LocalRpcException Conflict(string message = "Already exists")
        => new(message) { ErrorCode = CodeConflict };

    public static LocalRpcException Validation(string message)
        => new(message) { ErrorCode = CodeValidation };

    public static LocalRpcException Internal(string message = "An internal error occurred")
        => new(message) { ErrorCode = CodeInternal };
}

using StreamJsonRpc;

namespace H3xBoardServer.Rpc;

public static class RpcErrors
{
    // Error codes in the 4000-5000 range to avoid collision with JSON-RPC reserved codes
    public const int CodeNotFound = 4004;
    public const int CodeConflict = 4009;
    public const int CodePayloadTooLarge = 4013;
    public const int CodeValidation = 4022;

    public static LocalRpcException NotFound(string message = "Not found")
        => new(message) { ErrorCode = CodeNotFound };

    public static LocalRpcException Conflict(string message)
        => new(message) { ErrorCode = CodeConflict };

    public static LocalRpcException PayloadTooLarge(string message)
        => new(message) { ErrorCode = CodePayloadTooLarge };

    public static LocalRpcException Validation(string message)
        => new(message) { ErrorCode = CodeValidation };
}

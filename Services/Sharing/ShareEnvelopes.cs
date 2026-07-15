namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// A presenter-published message after minimal envelope validation. <see cref="RawJson"/> is the
/// verbatim message text — it is what gets stored (for snapshots) and relayed to viewers.
/// <see cref="FileIds"/> is only populated for <c>snapshot</c> envelopes.
/// </summary>
public readonly record struct ShareEnvelope(string Type, long Seq, IReadOnlyList<string>? FileIds, string RawJson);

/// <summary>
/// Envelope validation for presenter-published messages. The server reads only the envelope fields
/// <c>type</c> and <c>seq</c> — plus <c>fileIds</c> when the type is <c>snapshot</c> — and treats
/// everything else as opaque board content that is relayed verbatim.
/// </summary>
public static class ShareEnvelopes
{
    public const string SnapshotType = "snapshot";

    /// <summary>Validates one envelope. Throws a <c>4022</c> validation error when malformed.</summary>
    public static ShareEnvelope Parse(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            throw RpcErrors.Validation("Each message must be a JSON object envelope");

        if (!message.TryGetProperty("type", out var typeProperty)
            || typeProperty.ValueKind != JsonValueKind.String
            || typeProperty.GetString() is not { Length: > 0 } type)
            throw RpcErrors.Validation("Envelope is missing a non-empty string 'type' field");

        if (!message.TryGetProperty("seq", out var seqProperty)
            || seqProperty.ValueKind != JsonValueKind.Number
            || !seqProperty.TryGetInt64(out var seq))
            throw RpcErrors.Validation("Envelope is missing an integer 'seq' field");

        IReadOnlyList<string>? fileIds = null;
        if (type == SnapshotType)
            fileIds = ParseFileIds(message);

        return new ShareEnvelope(type, seq, fileIds, message.GetRawText());
    }

    private static List<string> ParseFileIds(JsonElement message)
    {
        var fileIds = new List<string>();
        if (!message.TryGetProperty("fileIds", out var fileIdsProperty))
            return fileIds;  // a snapshot without file references is fine

        if (fileIdsProperty.ValueKind != JsonValueKind.Array)
            throw RpcErrors.Validation("'fileIds' must be an array of strings");

        foreach (var element in fileIdsProperty.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
                throw RpcErrors.Validation("'fileIds' must be an array of strings");
            fileIds.Add(element.GetString()!);
        }

        return fileIds;
    }
}

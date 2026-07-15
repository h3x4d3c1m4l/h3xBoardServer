namespace H3xBoardServer.Rpc.Dtos;

/// <summary>
/// Parameters for <c>sharing.v1.start</c>. <paramref name="ResumeCode"/> optionally names a paused
/// session (from a previous connection of the same user) to re-bind instead of creating a new one.
/// </summary>
public record StartSharingRequest(string? ResumeCode = null);

/// <summary>
/// Parameters for <c>sharing.v1.publish</c>: a batch of opaque presenter envelopes, relayed
/// verbatim. The server reads only <c>type</c>, <c>seq</c>, and (for snapshots) <c>fileIds</c>.
/// </summary>
public record PublishShareRequest(List<JsonElement> Messages);

/// <summary>A presenter's view of its share session, returned by start/heartbeat.</summary>
public record ShareSessionDto(string Code, int ViewerCount);

/// <summary>Parameter object of the <c>sharing.v1.viewerCount</c> notification.</summary>
public record ViewerCountNotification(int Count);

/// <summary>Parameter object of the <c>sharing.v1.ended</c> notification.</summary>
public record SessionEndedNotification(string Reason);

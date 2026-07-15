using System.Text.Json;
using H3xBoardServer.Rpc;
using H3xBoardServer.Services.Sharing;
using StreamJsonRpc;

namespace H3xBoardServer.Tests;

public class ShareEnvelopesTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Parse_ExtractsTypeAndSeq_AndPreservesRawJson()
    {
        var raw = """{"v":1,"seq":42,"type":"strokeProgress","points":[[1,2],[3,4]],"color":4294901760}""";
        var envelope = ShareEnvelopes.Parse(Json(raw));

        Assert.Equal("strokeProgress", envelope.Type);
        Assert.Equal(42, envelope.Seq);
        Assert.Null(envelope.FileIds);  // only populated for snapshots
        Assert.Equal(raw, envelope.RawJson);
    }

    [Fact]
    public void Parse_Snapshot_ExtractsFileIds()
    {
        var raw = """{"v":1,"seq":7,"type":"snapshot","fileIds":["file-a","file-b"],"board":{"widgets":[]}}""";
        var envelope = ShareEnvelopes.Parse(Json(raw));

        Assert.Equal(ShareEnvelopes.SnapshotType, envelope.Type);
        Assert.NotNull(envelope.FileIds);
        Assert.Equal(["file-a", "file-b"], envelope.FileIds);
    }

    [Fact]
    public void Parse_Snapshot_WithoutFileIds_IsEmptyList()
    {
        var envelope = ShareEnvelopes.Parse(Json("""{"v":1,"seq":1,"type":"snapshot","board":{}}"""));
        Assert.NotNull(envelope.FileIds);
        Assert.Empty(envelope.FileIds);
    }

    [Theory]
    [InlineData("""{"v":1,"seq":1}""")]                                  // missing type
    [InlineData("""{"v":1,"seq":1,"type":""}""")]                        // empty type
    [InlineData("""{"v":1,"seq":1,"type":5}""")]                         // non-string type
    [InlineData("""{"v":1,"type":"clear"}""")]                           // missing seq
    [InlineData("""{"v":1,"seq":"1","type":"clear"}""")]                 // non-numeric seq
    [InlineData("""{"v":1,"seq":1.5,"type":"clear"}""")]                 // non-integer seq
    [InlineData("""{"v":1,"seq":1,"type":"snapshot","fileIds":"a"}""")]  // fileIds not an array
    [InlineData("""{"v":1,"seq":1,"type":"snapshot","fileIds":[1]}""")]  // fileIds not strings
    [InlineData("""[1,2,3]""")]                                          // not an object
    [InlineData("\"hello\"")]                                            // not an object
    public void Parse_RejectsMalformedEnvelopes(string json)
    {
        var ex = Assert.Throws<LocalRpcException>(() => ShareEnvelopes.Parse(Json(json)));
        Assert.Equal(RpcErrors.CodeValidation, ex.ErrorCode);
    }

    [Fact]
    public void Parse_DoesNotInspectPayload()
    {
        // Board content is opaque — even nonsense fields relay untouched.
        var raw = """{"v":1,"seq":3,"type":"widgetUpserted","widget":{"nested":{"deeply":[null,true,{"x":1}]}}}""";
        Assert.Equal(raw, ShareEnvelopes.Parse(Json(raw)).RawJson);
    }
}

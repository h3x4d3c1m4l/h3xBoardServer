using H3xBoardServer.Services.Sharing;

namespace H3xBoardServer.Tests;

public class ShareCodesTests
{
    [Fact]
    public void Generate_ProducesCodesOfTheRightShape()
    {
        for (var i = 0; i < 1000; i++)
        {
            var code = ShareCodes.Generate();
            Assert.Equal(ShareCodes.Length, code.Length);
            Assert.All(code, c => Assert.Contains(c, ShareCodes.Alphabet));
            Assert.True(ShareCodes.IsValid(code));
        }
    }

    [Fact]
    public void Generate_DoesNotObviouslyRepeat()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => ShareCodes.Generate()).ToHashSet();
        // 1000 draws from ~900M combinations — a collision here would be a broken RNG.
        Assert.Equal(1000, codes.Count);
    }

    [Theory]
    [InlineData("abc234", "ABC234")]
    [InlineData("ABC-234", "ABC234")]
    [InlineData(" ab c-2 34 ", "ABC234")]
    [InlineData("A-B-C-2-3-4", "ABC234")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_StripsSeparatorsAndUppercases(string? input, string expected)
    {
        Assert.Equal(expected, ShareCodes.Normalize(input));
    }

    [Theory]
    [InlineData("ABC234", true)]
    [InlineData("ABC23", false)]   // too short
    [InlineData("ABC2345", false)] // too long
    [InlineData("ABC230", false)]  // 0 not in alphabet
    [InlineData("ABC23O", false)]  // O not in alphabet
    [InlineData("ABC23I", false)]  // I not in alphabet
    [InlineData("abc234", false)]  // not normalized
    [InlineData("", false)]
    public void IsValid_ChecksLengthAndAlphabet(string code, bool expected)
    {
        Assert.Equal(expected, ShareCodes.IsValid(code));
    }
}

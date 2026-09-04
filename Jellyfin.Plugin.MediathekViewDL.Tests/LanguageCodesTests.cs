using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class LanguageCodesTests
{
    [Theory]
    [InlineData("eng", "eng")]
    [InlineData("ENG", "eng")]
    [InlineData("en", "eng")]
    [InlineData("en-GB", "eng")]
    [InlineData(" de ", "deu")]
    public void Normalize_ShouldProduceTheThreeLetterForm(string input, string expected)
    {
        Assert.Equal(expected, LanguageCodes.Normalize(input));
    }

    [Theory]
    [InlineData("ov")]
    [InlineData("und")]
    [InlineData("mul")]
    [InlineData("zxx")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    public void Normalize_ShouldReturnNull_ForAnythingThatNamesNoLanguage(string? input)
    {
        Assert.Null(LanguageCodes.Normalize(input));
        Assert.True(LanguageCodes.IsUndetermined(input));
    }

    [Fact]
    public void ParseList_ShouldDropWhatNamesNoLanguage()
    {
        var codes = LanguageCodes.ParseList("deu, ov, , eng, und, quatschcode");

        Assert.Equal(2, codes.Count);
        Assert.Contains("deu", codes);
        Assert.Contains("eng", codes);
    }
}

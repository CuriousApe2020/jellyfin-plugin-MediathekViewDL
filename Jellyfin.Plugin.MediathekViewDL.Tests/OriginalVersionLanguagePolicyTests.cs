using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class OriginalVersionLanguagePolicyTests
{
    [Fact]
    public void Decide_ShouldPreferTheBroadcastersOwnAnswer()
    {
        var decision = OriginalVersionLanguagePolicy.Decide("eng", "fra", allowUndefined: true);

        Assert.Equal("eng", decision.LanguageCode);
        Assert.False(decision.IsRefused);
    }

    [Fact]
    public void Decide_ShouldFallBackToTheConfiguredLanguage_WhenTheBroadcasterSaysNothing()
    {
        var decision = OriginalVersionLanguagePolicy.Decide(null, "eng", allowUndefined: true);

        Assert.Equal("eng", decision.LanguageCode);
    }

    [Theory]
    [InlineData("und")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Decide_ShouldTreatPlaceholdersAsNoAnswerAtAll(string? broadcasterAnswer)
    {
        var decision = OriginalVersionLanguagePolicy.Decide(broadcasterAnswer, "nld", allowUndefined: true);

        Assert.Equal("nld", decision.LanguageCode);
    }

    [Fact]
    public void Decide_ShouldTagUndetermined_WhenNothingIsKnownAndItIsAllowed()
    {
        var decision = OriginalVersionLanguagePolicy.Decide(null, null, allowUndefined: true);

        Assert.Equal("und", decision.LanguageCode);
        Assert.False(decision.IsRefused);
    }

    [Fact]
    public void Decide_ShouldRefuse_WhenNothingIsKnownAndUndeterminedIsNotAllowed()
    {
        var decision = OriginalVersionLanguagePolicy.Decide(null, "   ", allowUndefined: false);

        Assert.True(decision.IsRefused);
        Assert.Null(decision.LanguageCode);
        Assert.Equal(OriginalVersionLanguagePolicy.UndefinedRefusedMessage, decision.RefusalReason);
    }

    [Fact]
    public void Decide_ShouldNeverRefuse_WhenALanguageIsKnown()
    {
        var decision = OriginalVersionLanguagePolicy.Decide(null, "eng", allowUndefined: false);

        Assert.False(decision.IsRefused);
        Assert.Equal("eng", decision.LanguageCode);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("und", true)]
    [InlineData("UND", true)]
    [InlineData("eng", false)]
    public void IsUndefined_ShouldRecognizeThePlaceholder(string? languageCode, bool expected)
    {
        Assert.Equal(expected, OriginalVersionLanguagePolicy.IsUndefined(languageCode));
    }
}

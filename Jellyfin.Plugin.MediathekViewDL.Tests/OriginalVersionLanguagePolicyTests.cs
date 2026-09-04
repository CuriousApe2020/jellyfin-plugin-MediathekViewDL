using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class OriginalVersionLanguagePolicyTests
{
    [Fact]
    public void Decide_ShouldPreferTheBroadcastersOwnAnswer()
    {
        var decision = OriginalVersionLanguagePolicy.Decide("eng", "fra", UndefinedOriginalVersionHandling.UseFallbackLanguage);

        Assert.Equal("eng", decision.LanguageCode);
        Assert.False(decision.IsSkipped);
    }

    [Fact]
    public void Decide_ShouldFallBackToTheConfiguredLanguage_WhenTheBroadcasterSaysNothing()
    {
        var decision = OriginalVersionLanguagePolicy.Decide(null, "eng", UndefinedOriginalVersionHandling.UseFallbackLanguage);

        Assert.Equal("eng", decision.LanguageCode);
    }

    [Theory]
    [InlineData("und")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Decide_ShouldTreatPlaceholdersAsNoAnswerAtAll(string? broadcasterAnswer)
    {
        var decision = OriginalVersionLanguagePolicy.Decide(broadcasterAnswer, "nld", UndefinedOriginalVersionHandling.UseFallbackLanguage);

        Assert.Equal("nld", decision.LanguageCode);
    }

    [Fact]
    public void Decide_ShouldTagUndetermined_WhenNothingIsKnownAndThatIsWhatWasAskedFor()
    {
        var decision = OriginalVersionLanguagePolicy.Decide(null, null, UndefinedOriginalVersionHandling.StoreAsUndetermined);

        Assert.Equal("und", decision.LanguageCode);
        Assert.False(decision.IsSkipped);
    }

    [Fact]
    public void Decide_ShouldSkip_WhenNothingIsKnownAndSkippingWasAskedFor()
    {
        var decision = OriginalVersionLanguagePolicy.Decide(null, "   ", UndefinedOriginalVersionHandling.SkipTrack);

        Assert.True(decision.IsSkipped);
        Assert.Null(decision.LanguageCode);
        Assert.Equal(OriginalVersionLanguagePolicy.SkippedMessage, decision.SkipReason);
    }

    [Fact]
    public void Decide_ShouldNeverSkip_WhenTheBroadcasterNamesTheLanguage()
    {
        var decision = OriginalVersionLanguagePolicy.Decide("eng", null, UndefinedOriginalVersionHandling.SkipTrack);

        Assert.False(decision.IsSkipped);
        Assert.Equal("eng", decision.LanguageCode);
    }

    [Fact]
    public void Decide_ShouldIgnoreTheFallback_WhenSkippingWasAskedFor()
    {
        // The fallback code belongs to "use this language" - the other two modes must not borrow it.
        var decision = OriginalVersionLanguagePolicy.Decide(null, "eng", UndefinedOriginalVersionHandling.SkipTrack);

        Assert.True(decision.IsSkipped);
        Assert.Null(decision.LanguageCode);
    }

    [Fact]
    public void Decide_ShouldIgnoreTheFallback_WhenUndeterminedWasAskedFor()
    {
        var decision = OriginalVersionLanguagePolicy.Decide(null, "eng", UndefinedOriginalVersionHandling.StoreAsUndetermined);

        Assert.False(decision.IsSkipped);
        Assert.Equal("und", decision.LanguageCode);
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

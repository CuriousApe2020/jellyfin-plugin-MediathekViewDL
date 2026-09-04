using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class SecondaryAudioUrlHelperTests
{
    [Fact]
    public void DetectCandidates_ShouldReturnEmpty_WhenUrlIsNull()
    {
        var result = SecondaryAudioUrlHelper.DetectCandidates(null);
        Assert.Empty(result);
    }

    [Fact]
    public void DetectCandidates_ShouldReturnEmpty_WhenUrlDoesNotContainMainToken()
    {
        var result = SecondaryAudioUrlHelper.DetectCandidates("https://ard.de/video_sonstiges_1080p.mp4");
        Assert.Empty(result);
    }

    [Fact]
    public void DetectCandidates_ShouldReturnAllThreeVariants_WhenMainTokenIsPresent()
    {
        var result = SecondaryAudioUrlHelper.DetectCandidates("https://ard.de/video_sendeton_1080p.mp4");

        Assert.Equal(3, result.Count);
        Assert.Contains(result, c => c.Kind == SecondaryAudioKind.OriginalVersion && c.Url == "https://ard.de/video_originalversion_1080p.mp4" && c.LanguageCode == "und");
        Assert.Contains(result, c => c.Kind == SecondaryAudioKind.AudioDescription && c.Url == "https://ard.de/video_audiodeskription_1080p.mp4" && c.LanguageCode == "deu");
        Assert.Contains(result, c => c.Kind == SecondaryAudioKind.ClearSpeech && c.Url == "https://ard.de/video_klaresprache_1080p.mp4" && c.LanguageCode == "deu");
    }

    [Fact]
    public void DetectCandidates_ShouldMatchToken_CaseInsensitively()
    {
        var result = SecondaryAudioUrlHelper.DetectCandidates("https://ard.de/video_SendeTon_1080p.mp4");
        Assert.Equal(3, result.Count);
    }

    [Theory]
    [InlineData(SecondaryAudioKind.OriginalVersion)]
    [InlineData(SecondaryAudioKind.AudioDescription)]
    [InlineData(SecondaryAudioKind.ClearSpeech)]
    public void IsKindEnabled_ShouldCollectNothing_WithDefaultSettings(SecondaryAudioKind kind)
    {
        // Detection is off by default in both branches, so nothing is collected either way.
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(
            new BaseDownloadSettings(),
            new AccessibilitySettings(),
            kind,
            SecondaryAudioDetectionSource.UrlDerived));
    }

    [Fact]
    public void IsKindEnabled_ShouldKeepTheThreeKindsApart()
    {
        // Language versions are switched on, accessibility tracks are not - even though both
        // accessibility kinds are allowed, nothing looks for them.
        var download = new BaseDownloadSettings { DetectUndetectedSecondaryAudio = true };
        var accessibility = new AccessibilitySettings { AllowAudioDescription = true, DownloadClearSpeech = true };

        Assert.True(SecondaryAudioUrlHelper.IsKindEnabled(download, accessibility, SecondaryAudioKind.OriginalVersion, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(download, accessibility, SecondaryAudioKind.AudioDescription, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(download, accessibility, SecondaryAudioKind.ClearSpeech, SecondaryAudioDetectionSource.UrlDerived));

        // Audio description alone: neither "klare Sprache" nor language versions come along.
        var audioDescriptionOnly = new AccessibilitySettings
        {
            AllowAudioDescription = true,
            DownloadClearSpeech = true,
            DetectUndetectedAudioDescription = true,
        };

        Assert.True(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), audioDescriptionOnly, SecondaryAudioKind.AudioDescription, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), audioDescriptionOnly, SecondaryAudioKind.ClearSpeech, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), audioDescriptionOnly, SecondaryAudioKind.OriginalVersion, SecondaryAudioDetectionSource.UrlDerived));

        // ... and "klare Sprache" alone, the other way round.
        var clearSpeechOnly = new AccessibilitySettings
        {
            AllowAudioDescription = true,
            DownloadClearSpeech = true,
            DetectUndetectedClearSpeech = true,
        };

        Assert.True(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), clearSpeechOnly, SecondaryAudioKind.ClearSpeech, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), clearSpeechOnly, SecondaryAudioKind.AudioDescription, SecondaryAudioDetectionSource.UrlDerived));
    }

    [Fact]
    public void IsKindEnabled_ShouldStillHonourTheKindsOwnSwitch()
    {
        // Looking for a kind that is not wanted at all finds nothing.
        var accessibility = new AccessibilitySettings
        {
            AllowAudioDescription = false,
            DownloadClearSpeech = false,
            DetectUndetectedAudioDescription = true,
            DetectUndetectedClearSpeech = true,
        };

        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), accessibility, SecondaryAudioKind.AudioDescription, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), accessibility, SecondaryAudioKind.ClearSpeech, SecondaryAudioDetectionSource.UrlDerived));
    }

    [Fact]
    public void AnyCrossResultDetectionEnabled_ShouldFollowTheSameRules()
    {
        Assert.False(SecondaryAudioUrlHelper.AnyCrossResultDetectionEnabled(new BaseDownloadSettings(), new AccessibilitySettings()));

        // Switched on, but for a kind the subscription does not want at all.
        Assert.False(SecondaryAudioUrlHelper.AnyCrossResultDetectionEnabled(
            new BaseDownloadSettings(),
            new AccessibilitySettings { DetectCrossResultClearSpeech = true }));

        Assert.True(SecondaryAudioUrlHelper.AnyCrossResultDetectionEnabled(
            new BaseDownloadSettings(),
            new AccessibilitySettings { DownloadClearSpeech = true, DetectCrossResultClearSpeech = true }));

        Assert.True(SecondaryAudioUrlHelper.AnyCrossResultDetectionEnabled(
            new BaseDownloadSettings { DetectCrossResultAudioVariants = true },
            new AccessibilitySettings()));
    }

    [Fact]
    public void IsKindEnabled_ShouldDistinguishTheTwoDetectionSources()
    {
        var download = new BaseDownloadSettings { DetectCrossResultAudioVariants = true };
        var accessibility = new AccessibilitySettings();

        Assert.True(SecondaryAudioUrlHelper.IsKindEnabled(download, accessibility, SecondaryAudioKind.OriginalVersion, SecondaryAudioDetectionSource.CrossResult));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(download, accessibility, SecondaryAudioKind.OriginalVersion, SecondaryAudioDetectionSource.UrlDerived));
    }
}

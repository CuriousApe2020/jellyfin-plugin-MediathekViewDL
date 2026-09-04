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
    public void IsKindEnabled_ShouldKeepTheTwoBranchesApart()
    {
        // Language versions are switched on, accessibility tracks are not.
        var download = new BaseDownloadSettings { DetectUndetectedSecondaryAudio = true };
        var accessibility = new AccessibilitySettings { AllowAudioDescription = true, DownloadClearSpeech = true };

        Assert.True(SecondaryAudioUrlHelper.IsKindEnabled(download, accessibility, SecondaryAudioKind.OriginalVersion, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(download, accessibility, SecondaryAudioKind.AudioDescription, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(download, accessibility, SecondaryAudioKind.ClearSpeech, SecondaryAudioDetectionSource.UrlDerived));

        // ... and the other way round, including the kind switches inside the accessibility branch.
        var accessibilityOn = new AccessibilitySettings
        {
            AllowAudioDescription = true,
            DownloadClearSpeech = false,
            DetectUndetectedAccessibilityAudio = true,
        };

        Assert.True(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), accessibilityOn, SecondaryAudioKind.AudioDescription, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), accessibilityOn, SecondaryAudioKind.ClearSpeech, SecondaryAudioDetectionSource.UrlDerived));
        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(new BaseDownloadSettings(), accessibilityOn, SecondaryAudioKind.OriginalVersion, SecondaryAudioDetectionSource.UrlDerived));
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

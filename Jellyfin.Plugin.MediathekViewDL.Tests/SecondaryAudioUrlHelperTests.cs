using Jellyfin.Plugin.MediathekViewDL.Configuration.SubscriptionSettings;
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
    [InlineData(SecondaryAudioKind.OriginalVersion, true)]
    [InlineData(SecondaryAudioKind.AudioDescription, false)]
    [InlineData(SecondaryAudioKind.ClearSpeech, false)]
    public void IsKindEnabled_ShouldReflectDefaultSettings(SecondaryAudioKind kind, bool expected)
    {
        var settings = new BaseDownloadSettings();
        Assert.Equal(expected, SecondaryAudioUrlHelper.IsKindEnabled(settings, kind));
    }

    [Fact]
    public void IsKindEnabled_ShouldReflectExplicitSettings()
    {
        var settings = new BaseDownloadSettings
        {
            DownloadOriginalVersionAudio = false,
            DownloadAudioDescriptionAudio = true,
            DownloadClearSpeechAudio = true,
        };

        Assert.False(SecondaryAudioUrlHelper.IsKindEnabled(settings, SecondaryAudioKind.OriginalVersion));
        Assert.True(SecondaryAudioUrlHelper.IsKindEnabled(settings, SecondaryAudioKind.AudioDescription));
        Assert.True(SecondaryAudioUrlHelper.IsKindEnabled(settings, SecondaryAudioKind.ClearSpeech));
    }
}

using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class AudioLanguageSelectionTests
{
    [Fact]
    public void From_ShouldKeepEverything_InTheDefaultMode()
    {
        var selection = AudioLanguageSelection.From(new BaseDownloadSettings());

        Assert.True(selection.KeepsEverything);
        Assert.True(selection.Allows("eng"));
        Assert.True(selection.Allows("und"));
    }

    [Theory]
    [InlineData("deu, eng")]
    [InlineData("de,en")]
    [InlineData("DEU;ENG")]
    [InlineData(" deu   eng ")]
    public void Allows_ShouldAcceptTheListedLanguages_HoweverTheyWereTyped(string configured)
    {
        var selection = AudioLanguageSelection.From(new BaseDownloadSettings
        {
            AudioLanguageMode = AudioLanguageMode.Selected,
            SelectedAudioLanguages = configured,
        });

        Assert.True(selection.Allows("deu"));
        Assert.True(selection.Allows("eng"));
        Assert.True(selection.Allows("en-GB"));
        Assert.False(selection.Allows("fra"));
    }

    [Fact]
    public void Allows_ShouldNeverMatchAnUndeterminedLanguage()
    {
        // What happens to those is decided by UndefinedOriginalVersionHandling, not by the filter.
        var selection = AudioLanguageSelection.From(new BaseDownloadSettings
        {
            AudioLanguageMode = AudioLanguageMode.Selected,
            SelectedAudioLanguages = "deu, und",
        });

        Assert.False(selection.Allows("und"));
        Assert.False(selection.Allows(null));
        Assert.True(selection.Allows("deu"));
    }

    [Fact]
    public void From_ShouldReportAnEmptyFilter_WhenNothingUsableWasListed()
    {
        var selection = AudioLanguageSelection.From(new BaseDownloadSettings
        {
            AudioLanguageMode = AudioLanguageMode.Selected,
            SelectedAudioLanguages = "  ,  ",
        });

        Assert.True(selection.IsEmptyFilter);
        Assert.False(selection.Allows("deu"));
    }
}

using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class ChannelDefaultLanguageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ARD")]
    [InlineData("ZDF")]
    [InlineData("3Sat")]
    [InlineData("unknown-channel")]
    public void GetDefault_ShouldFallBackToGerman(string? channel)
    {
        Assert.Equal("deu", ChannelDefaultLanguage.GetDefault(channel));
    }

    // One case per arte channel the MediathekView crawler actually registers
    // (ArteCrawler plus ArteCrawler_FR/_EN/_ES/_PL/_IT). Missing any of these means items from
    // that channel are treated as German and land as a second full video next to the German
    // episode instead of as an additional audio track.
    [Theory]
    [InlineData("ARTE.DE", "deu")]
    [InlineData("ARTE.FR", "fra")]
    [InlineData("ARTE.EN", "eng")]
    [InlineData("ARTE.ES", "spa")]
    [InlineData("ARTE.IT", "ita")]
    [InlineData("ARTE.PL", "pol")]
    public void GetDefault_ShouldReturnTheChannelsOwnLanguage(string channel, string expected)
    {
        Assert.Equal(expected, ChannelDefaultLanguage.GetDefault(channel));
    }

    // The channel name arrives verbatim from the search index, so the lookup must not depend on
    // how it happens to be capitalised there.
    [Theory]
    [InlineData("arte.fr")]
    [InlineData("Arte.Fr")]
    [InlineData("ARTE.fr")]
    public void GetDefault_ShouldIgnoreCase(string channel)
    {
        Assert.Equal("fra", ChannelDefaultLanguage.GetDefault(channel));
    }
}

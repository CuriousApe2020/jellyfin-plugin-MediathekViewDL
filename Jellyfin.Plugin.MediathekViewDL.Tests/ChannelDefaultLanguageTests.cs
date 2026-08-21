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
    [InlineData("ARTE.DE")]
    [InlineData("unknown-channel")]
    public void GetDefault_ShouldFallBackToGerman(string? channel)
    {
        Assert.Equal("deu", ChannelDefaultLanguage.GetDefault(channel));
    }

    [Theory]
    [InlineData("ARTE.FR")]
    [InlineData("arte.fr")]
    [InlineData("Arte.Fr")]
    public void GetDefault_ShouldReturnFrenchForArteFr(string channel)
    {
        Assert.Equal("fra", ChannelDefaultLanguage.GetDefault(channel));
    }
}

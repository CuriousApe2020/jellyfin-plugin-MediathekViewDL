using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MediathekViewDL.Api.Models;
using Jellyfin.Plugin.MediathekViewDL.Api.Models.ResourceItem;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class AudioVariantGroupingServiceTests
{
    private static ResultItemDto MakeItem(string id, string title, string topic, string channel, DateTimeOffset timestamp, TimeSpan duration) => new()
    {
        Id = id,
        Title = title,
        Topic = topic,
        Channel = channel,
        Description = string.Empty,
        Timestamp = timestamp,
        Duration = duration,
        VideoUrls = new List<VideoUrlDto>(),
        SubtitleUrls = new List<SubtitleUrlDto>(),
        ExternalIds = new List<ExternalId>()
    };

    private static VideoInfo MakeInfo(
        string title,
        string topic,
        string language = "deu",
        bool hasAd = false,
        bool hasClearLanguage = false,
        bool hasSignLanguage = false) => new()
    {
        Title = title,
        Topic = topic,
        Language = language,
        HasAudiodescription = hasAd,
        HasClearLanguage = hasClearLanguage,
        HasSignLanguage = hasSignLanguage
    };

    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 20, 15, 0, TimeSpan.Zero);
    private static readonly TimeSpan BaseDuration = TimeSpan.FromMinutes(90);

    [Fact]
    public void GroupByEpisode_ShouldGroupArteChannelSplitAsOriginalVersion()
    {
        var main = (MakeItem("1", "Rendezvous mit einer Leiche", "Filme", "ARTE.DE", BaseTime, BaseDuration), MakeInfo("Rendezvous mit einer Leiche", "Filme", "deu"));
        var sibling = (MakeItem("2", "Rendez-vous avec la mort", "Filme", "ARTE.FR", BaseTime.AddHours(3), BaseDuration), MakeInfo("Rendezvous mit einer Leiche", "Filme", "fra"));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { main, sibling });

        var group = Assert.Single(groups);
        Assert.Equal("1", group.MainItem.Id);
        var secondary = Assert.Single(group.Secondaries);
        Assert.Equal("2", secondary.Item.Id);
        Assert.Equal(SecondaryAudioKind.OriginalVersion, secondary.Kind);
        Assert.Equal("fra", secondary.VideoInfo.Language);
    }

    [Fact]
    public void GroupByEpisode_ShouldGroupZdfLanguageSplitAcrossSiblingChannels()
    {
        var main = (MakeItem("1", "Die Doku", "Wissen", "ZDF", BaseTime, BaseDuration), MakeInfo("Die Doku", "Wissen", "deu"));
        var sibling = (MakeItem("2", "Die Doku", "Wissen", "ZDFNEO", BaseTime.AddHours(1), BaseDuration), MakeInfo("Die Doku", "Wissen", "eng"));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { main, sibling });

        var group = Assert.Single(groups);
        var secondary = Assert.Single(group.Secondaries);
        Assert.Equal("eng", secondary.VideoInfo.Language);
        Assert.Equal(SecondaryAudioKind.OriginalVersion, secondary.Kind);
    }

    [Fact]
    public void GroupByEpisode_ShouldDetectAudioDescriptionSibling()
    {
        var main = (MakeItem("1", "Der Film", "Kino", "ARTE.DE", BaseTime, BaseDuration), MakeInfo("Der Film", "Kino", "deu"));
        var sibling = (MakeItem("2", "Der Film", "Kino", "ARTE.DE", BaseTime.AddMinutes(30), BaseDuration + TimeSpan.FromMinutes(2)), MakeInfo("Der Film", "Kino", "deu", hasAd: true));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { main, sibling });

        var group = Assert.Single(groups);
        var secondary = Assert.Single(group.Secondaries);
        Assert.Equal(SecondaryAudioKind.AudioDescription, secondary.Kind);
    }

    [Fact]
    public void GroupByEpisode_ShouldNotGroupDifferentTopics()
    {
        var a = (MakeItem("1", "Titel", "Thema A", "ARTE.DE", BaseTime, BaseDuration), MakeInfo("Titel", "Thema A"));
        var b = (MakeItem("2", "Titel", "Thema B", "ARTE.FR", BaseTime, BaseDuration), MakeInfo("Titel", "Thema B", "fra"));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { a, b });

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Empty(g.Secondaries));
    }

    [Fact]
    public void GroupByEpisode_ShouldNotGroupWhenDurationDiffersTooMuch()
    {
        var a = (MakeItem("1", "Titel", "Thema", "ARTE.DE", BaseTime, TimeSpan.FromMinutes(90)), MakeInfo("Titel", "Thema"));
        var b = (MakeItem("2", "Titel", "Thema", "ARTE.FR", BaseTime, TimeSpan.FromMinutes(20)), MakeInfo("Titel", "Thema", "fra"));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { a, b });

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void GroupByEpisode_ShouldNotGroupWhenTimestampsAreTooFarApart()
    {
        var a = (MakeItem("1", "Titel", "Thema", "ARTE.DE", BaseTime, BaseDuration), MakeInfo("Titel", "Thema"));
        var b = (MakeItem("2", "Titel", "Thema", "ARTE.FR", BaseTime.AddDays(10), BaseDuration), MakeInfo("Titel", "Thema", "fra"));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { a, b });

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void GroupByEpisode_ShouldNotTreatIdenticalRerunAsSecondary()
    {
        // Same language, same flags - most likely a genuine rerun on a sibling channel, not a
        // distinct audio option, so it must not become a "secondary track".
        var a = (MakeItem("1", "Nachrichten", "News", "ZDF", BaseTime, BaseDuration), MakeInfo("Nachrichten", "News", "deu"));
        var b = (MakeItem("2", "Nachrichten", "News", "ZDFNEO", BaseTime, BaseDuration), MakeInfo("Nachrichten", "News", "deu"));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { a, b });

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Empty(g.Secondaries));
    }

    [Fact]
    public void GroupByEpisode_ShouldExcludeSignLanguageFromSecondaries()
    {
        var main = (MakeItem("1", "Titel", "Thema", "ARD", BaseTime, BaseDuration), MakeInfo("Titel", "Thema"));
        var signLanguage = (MakeItem("2", "Titel", "Thema", "ARD", BaseTime, BaseDuration), MakeInfo("Titel", "Thema", hasSignLanguage: true));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { main, signLanguage });

        // Sign language sibling stays out of the group entirely, so each row ends up in its own group.
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Empty(g.Secondaries));
    }

    [Fact]
    public void GroupByEpisode_ShouldPreferGermanNonAdRowAsMain()
    {
        var ad = (MakeItem("1", "Titel", "Thema", "ARTE.DE", BaseTime, BaseDuration), MakeInfo("Titel", "Thema", hasAd: true));
        var clean = (MakeItem("2", "Titel", "Thema", "ARTE.DE", BaseTime, BaseDuration), MakeInfo("Titel", "Thema"));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { ad, clean });

        var group = Assert.Single(groups);
        Assert.Equal("2", group.MainItem.Id);
        var secondary = Assert.Single(group.Secondaries);
        Assert.Equal("1", secondary.Item.Id);
        Assert.Equal(SecondaryAudioKind.AudioDescription, secondary.Kind);
    }

    [Fact]
    public void GroupByEpisode_ShouldGroupClusterOfThreeVariants()
    {
        var main = (MakeItem("1", "Titel", "Thema", "ARTE.DE", BaseTime, BaseDuration), MakeInfo("Titel", "Thema"));
        var ov = (MakeItem("2", "Titel", "Thema", "ARTE.FR", BaseTime.AddHours(2), BaseDuration), MakeInfo("Titel", "Thema", "fra"));
        var ad = (MakeItem("3", "Titel", "Thema", "ARTE.DE", BaseTime.AddMinutes(-30), BaseDuration), MakeInfo("Titel", "Thema", hasAd: true));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { main, ov, ad });

        var group = Assert.Single(groups);
        Assert.Equal("1", group.MainItem.Id);
        Assert.Equal(2, group.Secondaries.Count);
        Assert.Contains(group.Secondaries, s => s.Item.Id == "2" && s.Kind == SecondaryAudioKind.OriginalVersion);
        Assert.Contains(group.Secondaries, s => s.Item.Id == "3" && s.Kind == SecondaryAudioKind.AudioDescription);
    }

    [Fact]
    public void GroupByEpisode_SingleItemShouldReturnGroupWithNoSecondaries()
    {
        var only = (MakeItem("1", "Titel", "Thema", "ARD", BaseTime, BaseDuration), MakeInfo("Titel", "Thema"));

        var groups = AudioVariantGroupingService.GroupByEpisode(new List<(ResultItemDto, VideoInfo)> { only });

        var group = Assert.Single(groups);
        Assert.Equal("1", group.MainItem.Id);
        Assert.Empty(group.Secondaries);
    }
}

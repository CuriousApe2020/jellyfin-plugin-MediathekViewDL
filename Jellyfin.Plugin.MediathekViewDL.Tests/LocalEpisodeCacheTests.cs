using Jellyfin.Plugin.MediathekViewDL.Services.Library;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class LocalEpisodeCacheTests
{
    [Fact]
    public void Contains_SeasonAndEpisode_ReturnsTrue()
    {
        var cache = new LocalEpisodeCache();
        cache.Add(1, 1, null, "path/to/file.mp4", "deu");

        Assert.True(cache.Contains(1, 1, null, "deu"));
        Assert.True(cache.Contains(1, 1, 999, "deu")); // Should match on S/E regardless of abs
        Assert.False(cache.Contains(1, 1, null, "eng")); // Different language
    }

    [Fact]
    public void Contains_AbsoluteEpisode_ReturnsTrue()
    {
        var cache = new LocalEpisodeCache();
        cache.Add(null, null, 10, "path/to/file.mp4", "deu");

        Assert.True(cache.Contains(null, null, 10, "deu"));
        Assert.True(cache.Contains(99, 99, 10, "deu"));
        Assert.False(cache.Contains(null, null, 10, "eng")); // Different language
    }

    [Fact]
    public void Contains_MixedData_ReturnsCorrectly()
    {
        var cache = new LocalEpisodeCache();
        // Add S1E1
        cache.Add(1, 1, null, "path/to/file.mp4", "deu");
        // Add Abs 5
        cache.Add(null, null, 5, "path/to/file.mp4", "eng");

        // Match S1E1
        Assert.True(cache.Contains(1, 1, null, "deu"));
        
        // Match Abs 5
        Assert.True(cache.Contains(null, null, 5, "eng"));
        
        // Mixed language checks
        Assert.False(cache.Contains(1, 1, null, "eng"));
        Assert.False(cache.Contains(null, null, 5, "deu"));

        // Match S1E1 with wrong Abs (should match because S/E matches)
        Assert.True(cache.Contains(1, 1, 999, "deu"));

        // Match Abs 5 with wrong S/E (should match because Abs matches)
        Assert.True(cache.Contains(99, 99, 5, "eng"));

        // No match
        Assert.False(cache.Contains(1, 2, null, "deu"));
        Assert.False(cache.Contains(null, null, 6, "eng"));
    }

    [Fact]
    public void TryGetEpisodeVideo_FindsEpisodeRecordedUnderADifferentLanguage()
    {
        // Arrange: the episode is on disk in English only - exactly the case that makes the
        // language-keyed Contains() say "not a duplicate" for the German variant.
        var cache = new LocalEpisodeCache();
        cache.Add(1, 1, null, "/media/S01E01 - Title.mkv", "eng");

        var german = new VideoInfo { Title = "Title", SeasonNumber = 1, EpisodeNumber = 1, Language = "deu" };

        // Act
        var found = cache.TryGetEpisodeVideo(german, out var path, out var languages);

        // Assert
        Assert.False(cache.Contains(german));
        Assert.True(found);
        Assert.Equal("/media/S01E01 - Title.mkv", path);
        Assert.Contains("eng", languages);
        Assert.DoesNotContain("deu", languages);
    }

    [Fact]
    public void TryGetEpisodeVideo_ReportsLanguageAlreadyPresentAsASidecar()
    {
        // Arrange: a previous run already attached the German track next to the English video.
        var cache = new LocalEpisodeCache();
        cache.Add(1, 1, null, "/media/S01E01 - Title.mkv", "eng");
        cache.Add(1, 1, null, "/media/S01E01 - Title.deu.mka", "deu", isSidecarAudio: true);

        var german = new VideoInfo { Title = "Title", SeasonNumber = 1, EpisodeNumber = 1, Language = "deu" };

        // Act
        var found = cache.TryGetEpisodeVideo(german, out var path, out var languages);

        // Assert: the sidecar must not become the anchor, but its language must count as present
        // - otherwise the same track would be re-fetched on every run.
        Assert.True(found);
        Assert.Equal("/media/S01E01 - Title.mkv", path);
        Assert.Contains("deu", languages);
        Assert.Contains("eng", languages);
    }

    [Fact]
    public void TryGetEpisodeVideo_ReturnsFalse_WhenOnlyASidecarExists()
    {
        // Arrange: no video to attach anything to.
        var cache = new LocalEpisodeCache();
        cache.Add(1, 1, null, "/media/S01E01 - Title.deu.mka", "deu", isSidecarAudio: true);

        // Act
        var found = cache.TryGetEpisodeVideo(
            new VideoInfo { Title = "Title", SeasonNumber = 1, EpisodeNumber = 1, Language = "eng" },
            out _,
            out _);

        // Assert
        Assert.False(found);
    }

    [Fact]
    public void TryGetEpisodeVideo_MatchesOnAbsoluteNumbering()
    {
        // Arrange
        var cache = new LocalEpisodeCache();
        cache.Add(null, null, 7, "/media/Title - 07.mkv", "eng");

        // Act
        var found = cache.TryGetEpisodeVideo(
            new VideoInfo { Title = "Title", AbsoluteEpisodeNumber = 7, Language = "deu" },
            out var path,
            out _);

        // Assert
        Assert.True(found);
        Assert.Equal("/media/Title - 07.mkv", path);
    }

    [Fact]
    public void ContainsFile_FindsAFileTheScanSaw_EvenWithoutAnyEpisodeNumbering()
    {
        // Arrange: a film. Nothing in its name yields a season, episode or absolute number, so it
        // reaches none of the numbering indexes - which is why duplicate detection used to be blind
        // to entire film libraries.
        var cache = new LocalEpisodeCache();
        cache.AddFile("/media/Filme/Match Point/Match Point.mkv");

        // Assert
        Assert.Equal(0, cache.SeasonEpisodeCount);
        Assert.Equal(0, cache.AbsoluteEpisodeCount);
        Assert.Equal(1, cache.FileCount);
        Assert.True(cache.ContainsFile("/media/Filme/Match Point/Match Point.mkv"));
        Assert.False(cache.ContainsFile("/media/Filme/Match Point/Match Point.eng.mka"));
    }

    [Fact]
    public void ContainsFile_MatchesTheSameFileWrittenADifferentWay()
    {
        // Arrange: the two sides reach this check differently - one path comes from enumerating a
        // directory, the other from composing a name - so a redundant separator or a "." segment
        // must not make one file look like two.
        var cache = new LocalEpisodeCache();
        cache.AddFile("/media/Filme/Match Point/Match Point.mkv");

        // Assert
        Assert.True(cache.ContainsFile("/media/Filme/./Match Point/Match Point.mkv"));
        Assert.True(cache.ContainsFile("/media/Filme//Match Point/Match Point.mkv"));
    }

    [Fact]
    public void ContainsFile_IgnoresNothingAndNonsense()
    {
        var cache = new LocalEpisodeCache();
        cache.AddFile("/media/Filme/Match Point/Match Point.mkv");

        Assert.False(cache.ContainsFile(null));
        Assert.False(cache.ContainsFile(string.Empty));
        Assert.False(cache.ContainsFile("   "));
    }
}

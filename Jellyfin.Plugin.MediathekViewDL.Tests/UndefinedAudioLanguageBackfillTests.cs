using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Clients;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class UndefinedAudioLanguageBackfillTests : IDisposable
{
    private readonly string _directory;
    private readonly Mock<IFFmpegService> _ffmpegServiceMock;
    private readonly UndefinedAudioLanguageBackfill _backfill;

    public UndefinedAudioLanguageBackfillTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mvdl-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _ffmpegServiceMock = new Mock<IFFmpegService>();

        // Stand in for ffmpeg: write the output file the real one would produce.
        _ffmpegServiceMock
            .Setup(x => x.RetagAudioLanguageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string output, string _, bool _, CancellationToken _) =>
            {
                File.WriteAllText(output, "retagged");
                return Task.FromResult(true);
            });

        _backfill = new UndefinedAudioLanguageBackfill(
            _ffmpegServiceMock.Object,
            new Mock<ILogger<UndefinedAudioLanguageBackfill>>().Object);
    }

    [Fact]
    public async Task BackfillAsync_ShouldRenameAndRetagUndeterminedTracks()
    {
        var season = Path.Combine(_directory, "Staffel 1");
        Directory.CreateDirectory(season);
        var source = Path.Combine(season, "S01E01 - Folge.und.mka");
        await File.WriteAllTextAsync(source, "audio");

        var updated = await _backfill.BackfillAsync(_directory, "eng", recursive: true, CancellationToken.None);

        Assert.Equal(1, updated);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(season, "S01E01 - Folge.eng.mka")));
    }

    [Fact]
    public async Task BackfillAsync_ShouldNotDescendIntoSubdirectories_WhenNotRecursive()
    {
        var season = Path.Combine(_directory, "Staffel 1");
        Directory.CreateDirectory(season);
        var nested = Path.Combine(season, "S01E01 - Folge.und.mka");
        await File.WriteAllTextAsync(nested, "audio");

        var updated = await _backfill.BackfillAsync(_directory, "eng", recursive: false, CancellationToken.None);

        Assert.Equal(0, updated);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task BackfillAsync_ShouldKeepTheOriginal_WhenTheTargetAlreadyExists()
    {
        var source = Path.Combine(_directory, "Folge.und.mka");
        var existing = Path.Combine(_directory, "Folge.eng.mka");
        await File.WriteAllTextAsync(source, "audio");
        await File.WriteAllTextAsync(existing, "older track");

        var updated = await _backfill.BackfillAsync(_directory, "eng", recursive: true, CancellationToken.None);

        Assert.Equal(0, updated);
        Assert.True(File.Exists(source));
        Assert.Equal("older track", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task BackfillAsync_ShouldKeepTheOriginal_WhenFfmpegFails()
    {
        _ffmpegServiceMock
            .Setup(x => x.RetagAudioLanguageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var source = Path.Combine(_directory, "Folge.und.mka");
        await File.WriteAllTextAsync(source, "audio");

        var updated = await _backfill.BackfillAsync(_directory, "eng", recursive: true, CancellationToken.None);

        Assert.Equal(0, updated);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(_directory, "Folge.eng.mka")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("und")]
    public async Task BackfillAsync_ShouldDoNothing_WithoutARealLanguage(string? languageCode)
    {
        var source = Path.Combine(_directory, "Folge.und.mka");
        await File.WriteAllTextAsync(source, "audio");

        var updated = await _backfill.BackfillAsync(_directory, languageCode, recursive: true, CancellationToken.None);

        Assert.Equal(0, updated);
        Assert.True(File.Exists(source));
        _ffmpegServiceMock.Verify(
            x => x.RetagAudioLanguageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BackfillAsync_ShouldDoNothing_ForAMissingDirectory()
    {
        var updated = await _backfill.BackfillAsync(Path.Combine(_directory, "gibt-es-nicht"), "eng", recursive: true, CancellationToken.None);

        Assert.Equal(0, updated);
    }

    [Fact]
    public async Task BackfillAsync_ShouldNotComplain_WhenAConcurrentPassFinishesTheSameFile()
    {
        var source = Path.Combine(_directory, "Folge.und.mka");
        var destination = Path.Combine(_directory, "Folge.eng.mka");
        await File.WriteAllTextAsync(source, "audio");

        // Two subscription passes can walk the same library at once. This one stands in for the
        // other finishing while ffmpeg is still running here: it writes the destination and takes
        // the source away, exactly as this pass is about to.
        _ffmpegServiceMock
            .Setup(x => x.RetagAudioLanguageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns((string input, string output, string _, bool _, CancellationToken _) =>
            {
                File.WriteAllText(output, "retagged");
                File.WriteAllText(destination, "written by the other pass");
                File.Delete(input);
                return Task.FromResult(true);
            });

        var updated = await _backfill.BackfillAsync(_directory, "eng", recursive: true, CancellationToken.None);

        // The library ends up as intended either way; what must not happen is a lost file or a
        // temporary one left lying around.
        Assert.Equal(0, updated);
        Assert.Equal("written by the other pass", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(_directory, "*.mvdl-tmp"));
    }

    [Fact]
    public async Task BackfillAsync_ShouldNotShareOneTemporaryFileAcrossTracks()
    {
        var first = Path.Combine(_directory, "Folge 1.und.mka");
        var second = Path.Combine(_directory, "Folge 2.und.mka");
        await File.WriteAllTextAsync(first, "audio");
        await File.WriteAllTextAsync(second, "audio");

        var temporaryPaths = new List<string>();
        _ffmpegServiceMock
            .Setup(x => x.RetagAudioLanguageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string output, string _, bool _, CancellationToken _) =>
            {
                temporaryPaths.Add(output);
                File.WriteAllText(output, "retagged");
                return Task.FromResult(true);
            });

        await _backfill.BackfillAsync(_directory, "eng", recursive: true, CancellationToken.None);

        Assert.Equal(2, temporaryPaths.Count);
        Assert.Equal(temporaryPaths.Count, temporaryPaths.Distinct(StringComparer.Ordinal).Count());
        Assert.All(temporaryPaths, path => Assert.EndsWith(".mvdl-tmp", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task BackfillEpisodeAsync_ShouldRenameTheTrackNextToOneVideo()
    {
        var video = Path.Combine(_directory, "S01E01 - Folge.mkv");
        var track = Path.Combine(_directory, "S01E01 - Folge.und.mka");
        await File.WriteAllTextAsync(video, "video");
        await File.WriteAllTextAsync(track, "audio");

        var updated = await _backfill.BackfillEpisodeAsync(video, "eng", CancellationToken.None);

        Assert.True(updated);
        Assert.False(File.Exists(track));
        Assert.True(File.Exists(Path.Combine(_directory, "S01E01 - Folge.eng.mka")));
        Assert.True(File.Exists(video));
    }

    [Fact]
    public async Task BackfillEpisodeAsync_ShouldDoNothing_WhenThereIsNoUndeterminedTrack()
    {
        var video = Path.Combine(_directory, "S01E02 - Folge.mkv");
        await File.WriteAllTextAsync(video, "video");

        Assert.False(await _backfill.BackfillEpisodeAsync(video, "eng", CancellationToken.None));
    }

    [Fact]
    public async Task BackfillEpisodeAsync_ShouldDoNothing_WithoutARealLanguage()
    {
        var video = Path.Combine(_directory, "S01E03 - Folge.mkv");
        var track = Path.Combine(_directory, "S01E03 - Folge.und.mka");
        await File.WriteAllTextAsync(video, "video");
        await File.WriteAllTextAsync(track, "audio");

        Assert.False(await _backfill.BackfillEpisodeAsync(video, "und", CancellationToken.None));
        Assert.True(File.Exists(track));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}

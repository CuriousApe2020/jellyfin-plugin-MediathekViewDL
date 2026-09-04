using System;
using System.IO;
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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}

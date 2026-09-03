using System;
using System.IO;
using Jellyfin.Plugin.MediathekViewDL.Services.Library;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class LocalMediaScannerTests : IDisposable
{
    private readonly string _directory;
    private readonly Mock<IVideoParser> _videoParserMock;
    private readonly Mock<ILanguageDetectionService> _languageDetectionServiceMock;
    private readonly LocalMediaScanner _scanner;

    public LocalMediaScannerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"mvdl_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "Match Point.mkv"), string.Empty);

        _videoParserMock = new Mock<IVideoParser>();
        _languageDetectionServiceMock = new Mock<ILanguageDetectionService>();

        _scanner = new LocalMediaScanner(
            Mock.Of<ILogger<LocalMediaScanner>>(),
            _videoParserMock.Object,
            _languageDetectionServiceMock.Object);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    [Fact]
    public void ScanDirectory_ReadsTheDiskOnlyOnce_ForRepeatedCalls()
    {
        // The same library tree gets scanned repeatedly within one subscription run: in a real log
        // /media/Serien (21440 files) was walked three times and /media/Filme three times, 30.6 of
        // the 48.7 seconds spent scanning. Repeated calls must come back from what was already read.
        var first = _scanner.ScanDirectory(_directory, "Filme");
        var second = _scanner.ScanDirectory(_directory, "Filme");

        Assert.Same(first, second);
    }

    [Fact]
    public void ScanDirectory_ReadsAgain_ForADifferentSeriesName()
    {
        // The series name is parsing context - the same folder read on behalf of another
        // subscription can yield different episode numbers - so it is part of the key.
        var forFilme = _scanner.ScanDirectory(_directory, "Filme");
        var forSerien = _scanner.ScanDirectory(_directory, "Serien");

        Assert.NotSame(forFilme, forSerien);
    }

    [Fact]
    public void InvalidateCache_ForcesTheNextScanToReadTheDiskAgain()
    {
        // What keeps a subscription run from starting on top of what an earlier run saw. Between
        // two runs the library can have changed in ways nothing here would notice.
        var first = _scanner.ScanDirectory(_directory, "Filme");

        _scanner.InvalidateCache();
        var afterInvalidate = _scanner.ScanDirectory(_directory, "Filme");

        Assert.NotSame(first, afterInvalidate);
    }

    [Fact]
    public void ScanDirectory_SeesAFileEvenWhenItsNameYieldsNoEpisodeNumber()
    {
        // Guards the claim the cache test makes in the abstract against a real directory: the
        // parser returns nothing for a film title, and the file still has to be recorded.
        _videoParserMock
            .Setup(x => x.ParseVideoInfo(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((VideoInfo?)null);

        var cache = _scanner.ScanDirectory(_directory, "Filme");

        Assert.Equal(0, cache.SeasonEpisodeCount);
        Assert.Equal(0, cache.AbsoluteEpisodeCount);
        Assert.True(cache.ContainsFile(Path.Combine(_directory, "Match Point.mkv")));
    }
}

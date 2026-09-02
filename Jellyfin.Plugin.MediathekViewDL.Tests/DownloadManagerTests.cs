using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Clients;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Handlers;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Models;
using Jellyfin.Plugin.MediathekViewDL.Services.Library;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Jellyfin.Plugin.MediathekViewDL.Services.Metadata;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class DownloadManagerTests
{
    private readonly Mock<ILogger<DownloadManager>> _loggerMock;
    private readonly Mock<INfoService> _nfoServiceMock;
    private readonly Mock<IFileDownloader> _fileDownloaderMock;
    private readonly Mock<IStrmValidationService> _validationServiceMock;
    private readonly DownloadManager _downloadManager;

    public DownloadManagerTests()
    {
        _loggerMock = new Mock<ILogger<DownloadManager>>();
        _nfoServiceMock = new Mock<INfoService>();
        _fileDownloaderMock = new Mock<IFileDownloader>();
        _validationServiceMock = new Mock<IStrmValidationService>();

        var handler = _fileDownloaderMock.As<IDownloadHandler>();
        handler.Setup(h => h.CanHandle(It.IsAny<DownloadType>())).Returns(true);
        handler.Setup(h => h.ExecuteAsync(
                It.IsAny<DownloadItem>(),
                It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _downloadManager = new DownloadManager(
            _loggerMock.Object,
            _nfoServiceMock.Object,
            new[] { handler.Object },
            _validationServiceMock.Object);
    }

    private static DownloadJob CreateJob(string sourceUrl, string destPath, DownloadType type = DownloadType.SubtitleDownload, IReadOnlyList<string>? fallbackSourceUrls = null)
    {
        return new DownloadJob
        {
            ItemId = "test-item",
            ItemInfo = new VideoInfo { Title = "Test Video" },
            Title = "Test Video",
            DownloadItems =
            {
                new DownloadItem
                {
                    SourceUrl = sourceUrl,
                    FallbackSourceUrls = fallbackSourceUrls,
                    DestinationPath = destPath,
                    JobType = type
                }
            }
        };
    }

    [Fact]
    public async Task ExecuteJobAsync_ShouldStopAndNotLogAnError_WhenValidationIsCancelled()
    {
        // Arrange: reproduces a real server log - the user hit "cancel all downloads" while a job
        // was mid-flight, the cancellation surfaced out of URL validation as a TaskCanceledException,
        // and the general catch treated it as a failed URL: an [ERR] for a user action, and the job
        // ground on through its remaining items.
        var videoPath = Path.Combine(Path.GetTempPath(), $"video_{Guid.NewGuid():N}.mp4");
        var subtitlePath = Path.Combine(Path.GetTempPath(), $"subs_{Guid.NewGuid():N}.ttml");

        var job = CreateJob("https://zdf.de/video.mp4", videoPath, DownloadType.FFmpegDownload);
        job.DownloadItems.Add(new DownloadItem
        {
            SourceUrl = "https://zdf.de/subs.ttml",
            DestinationPath = subtitlePath,
            JobType = DownloadType.SubtitleDownload
        });

        using var cts = new CancellationTokenSource();

        // Cancel *during* validation, exactly as the real cancellation arrived - pre-cancelling
        // would just trip the loop's top-of-iteration check and never reach the code under test.
        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new TaskCanceledException("A task was canceled."));

        // Act / Assert: the cancellation must surface as such, not be reported as a URL failure.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), cts.Token));

        _fileDownloaderMock.As<IDownloadHandler>().Verify(
            h => h.ExecuteAsync(It.IsAny<DownloadItem>(), It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task ExecuteJobAsync_ShouldNotProcessRemainingItems_WhenAHandlerIsCancelled()
    {
        // Arrange: the video handler is cancelled partway; the subtitle item behind it must not
        // then be picked up and validated as if nothing had happened.
        var videoPath = Path.Combine(Path.GetTempPath(), $"video_{Guid.NewGuid():N}.mp4");
        var subtitlePath = Path.Combine(Path.GetTempPath(), $"subs_{Guid.NewGuid():N}.ttml");

        var job = CreateJob("https://zdf.de/video.mp4", videoPath, DownloadType.FFmpegDownload);
        job.DownloadItems.Add(new DownloadItem
        {
            SourceUrl = "https://zdf.de/subs.ttml",
            DestinationPath = subtitlePath,
            JobType = DownloadType.SubtitleDownload
        });

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using var cts = new CancellationTokenSource();

        _fileDownloaderMock.As<IDownloadHandler>()
            .Setup(h => h.ExecuteAsync(
                It.IsAny<DownloadItem>(),
                It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), cts.Token));

        // Exactly one item was reached; the one behind it must not have been validated.
        _validationServiceMock.Verify(
            s => s.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task ExecuteJobAsync_ShouldStillWriteNfo_WhenOnlyASubtitleSidecarFailed()
    {
        // Arrange: a video item that succeeds plus a subtitle item that fails.
        var videoPath = Path.Combine(Path.GetTempPath(), $"video_{Guid.NewGuid():N}.mp4");
        var subtitlePath = Path.Combine(Path.GetTempPath(), $"subs_{Guid.NewGuid():N}.ttml");
        var nfoPath = Path.Combine(Path.GetTempPath(), $"nfo_{Guid.NewGuid():N}.nfo");

        var job = CreateJob("https://ard.de/video.mp4", videoPath, DownloadType.FFmpegDownload);
        job.DownloadItems.Add(new DownloadItem
        {
            SourceUrl = "https://ard.de/subs.ttml",
            DestinationPath = subtitlePath,
            JobType = DownloadType.SubtitleDownload
        });
        job.NfoMetadata = new NfoDTO { FilePath = nfoPath };

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = _fileDownloaderMock.As<IDownloadHandler>();
        handler.Setup(h => h.ExecuteAsync(
                It.Is<DownloadItem>(i => i.JobType == DownloadType.SubtitleDownload),
                It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

        // Assert: the job as a whole failed, but the video landed - so its NFO must still be written.
        Assert.False(result.Success);
        _nfoServiceMock.Verify(n => n.CreateNfo(It.IsAny<NfoDTO>()), Times.Once());
    }

    [Fact]
    public async Task ExecuteJobAsync_ShouldNotWriteNfo_WhenTheMediaItemItselfFailed()
    {
        // Arrange
        var videoPath = Path.Combine(Path.GetTempPath(), $"video_{Guid.NewGuid():N}.mp4");
        var nfoPath = Path.Combine(Path.GetTempPath(), $"nfo_{Guid.NewGuid():N}.nfo");

        var job = CreateJob("https://ard.de/video.mp4", videoPath, DownloadType.FFmpegDownload);
        job.NfoMetadata = new NfoDTO { FilePath = nfoPath };

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileDownloaderMock.As<IDownloadHandler>()
            .Setup(h => h.ExecuteAsync(
                It.IsAny<DownloadItem>(),
                It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

        // Assert: no media landed, so there is nothing for an NFO to describe.
        Assert.False(result.Success);
        _nfoServiceMock.Verify(n => n.CreateNfo(It.IsAny<NfoDTO>()), Times.Never());
    }

    [Fact]
    public async Task ExecuteJobAsync_ShouldAbortTheWholeJob_WhenWritingIsNotPermitted()
    {
        // Arrange: reproduces a real server log - the library directory was not writable for the
        // user Jellyfin runs as, and every single item of every single episode ran into it in turn,
        // each logging its own stack trace. The video item fails here; the subtitle behind it
        // targets the same directory and cannot possibly fare better.
        var videoPath = Path.Combine(Path.GetTempPath(), $"video_{Guid.NewGuid():N}.mp4");
        var subtitlePath = Path.Combine(Path.GetTempPath(), $"subs_{Guid.NewGuid():N}.ttml");
        var nfoPath = Path.Combine(Path.GetTempPath(), $"nfo_{Guid.NewGuid():N}.nfo");

        var job = CreateJob("https://ard.de/video.mp4", videoPath, DownloadType.FFmpegDownload);
        job.DownloadItems.Add(new DownloadItem
        {
            SourceUrl = "https://ard.de/subs.ttml",
            DestinationPath = subtitlePath,
            JobType = DownloadType.SubtitleDownload
        });
        job.NfoMetadata = new NfoDTO { FilePath = nfoPath };

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var deniedMessage = $"Access to the path '{videoPath}' is denied.";
        _fileDownloaderMock.As<IDownloadHandler>()
            .Setup(h => h.ExecuteAsync(
                It.Is<DownloadItem>(i => i.JobType == DownloadType.FFmpegDownload),
                It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException(deniedMessage));

        // Act
        var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

        // Assert: the attempt is abandoned outright rather than ground through item by item.
        Assert.False(result.Success);

        _fileDownloaderMock.As<IDownloadHandler>().Verify(
            h => h.ExecuteAsync(
                It.Is<DownloadItem>(i => i.JobType == DownloadType.SubtitleDownload),
                It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()),
            Times.Never());

        // The NFO would only be a second failure for the same cause.
        _nfoServiceMock.Verify(n => n.CreateNfo(It.IsAny<NfoDTO>()), Times.Never());

        // And the reason has to reach the UI - "Download fehlgeschlagen" would send the user
        // looking at the broadcaster instead of at their own file permissions.
        var failure = Assert.Single(result.ItemResults);
        Assert.False(failure.Success);
        Assert.NotNull(failure.ErrorMessage);
        Assert.Contains("nicht beschreibbar", failure.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(deniedMessage, failure.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteJobAsync_ShouldKeepProcessingRemainingItems_WhenAnItemFailsForAnyOtherReason()
    {
        // Arrange: the counterpart to the test above - only a non-writable destination aborts the
        // job. An ordinary failure on the video (a dead CDN URL, say) must still leave the
        // subtitle its own attempt, exactly as before.
        var videoPath = Path.Combine(Path.GetTempPath(), $"video_{Guid.NewGuid():N}.mp4");
        var subtitlePath = Path.Combine(Path.GetTempPath(), $"subs_{Guid.NewGuid():N}.ttml");

        var job = CreateJob("https://ard.de/video.mp4", videoPath, DownloadType.FFmpegDownload);
        job.DownloadItems.Add(new DownloadItem
        {
            SourceUrl = "https://ard.de/subs.ttml",
            DestinationPath = subtitlePath,
            JobType = DownloadType.SubtitleDownload
        });

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileDownloaderMock.As<IDownloadHandler>()
            .Setup(h => h.ExecuteAsync(
                It.Is<DownloadItem>(i => i.JobType == DownloadType.FFmpegDownload),
                It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(2, result.ItemResults.Count);
    }

    [Fact]
    public async Task ExecuteJobAsync_ValidationReturnsFalse_SkipsHandlerAndReturnsFalse()
    {
        // Arrange
        var destPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.tmp");
        var job = CreateJob("https://ard.de/deleted.mp4", destPath);

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync("https://ard.de/deleted.mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        _fileDownloaderMock.As<IDownloadHandler>().Verify(
            h => h.ExecuteAsync(It.IsAny<DownloadItem>(), It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task ExecuteJobAsync_ValidationThrowsException_SkipsHandlerAndReturnsFalse()
    {
        // Arrange
        var destPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.tmp");
        var job = CreateJob("https://ard.de/video.mp4", destPath);

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync("https://ard.de/video.mp4", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Server returned 404"));

        // Act
        var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        _fileDownloaderMock.As<IDownloadHandler>().Verify(
            h => h.ExecuteAsync(It.IsAny<DownloadItem>(), It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task ExecuteJobAsync_ValidationSucceeds_ExecutesHandler()
    {
        // Arrange
        var destPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.tmp");
        var job = CreateJob("https://ard.de/video.mp4", destPath);

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync("https://ard.de/video.mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        _fileDownloaderMock.As<IDownloadHandler>().Verify(
            h => h.ExecuteAsync(
                It.Is<DownloadItem>(i => i.SourceUrl == "https://ard.de/video.mp4"),
                job,
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task ExecuteJobAsync_PrimaryUrlExpiredWhileQueued_FallsBackToStillValidFallback()
    {
        // Arrange: simulates a URL that validated fine at discovery time but has since expired
        // while the job sat in the (currently strictly-serial) download queue - a lower-quality
        // sibling from the same search result is still reachable.
        var destPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.tmp");
        var job = CreateJob(
            "https://ard.de/expired-hd.mp4",
            destPath,
            fallbackSourceUrls: new[] { "https://ard.de/still-valid-sd.mp4" });

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync("https://ard.de/expired-hd.mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync("https://ard.de/still-valid-sd.mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        _fileDownloaderMock.As<IDownloadHandler>().Verify(
            h => h.ExecuteAsync(
                It.Is<DownloadItem>(i => i.SourceUrl == "https://ard.de/still-valid-sd.mp4"),
                job,
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task ExecuteJobAsync_PrimaryAndAllFallbackUrlsInvalid_SkipsHandlerAndReturnsFalse()
    {
        // Arrange
        var destPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.tmp");
        var job = CreateJob(
            "https://ard.de/expired-hd.mp4",
            destPath,
            fallbackSourceUrls: new[] { "https://ard.de/also-expired-sd.mp4" });

        _validationServiceMock
            .Setup(s => s.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        _fileDownloaderMock.As<IDownloadHandler>().Verify(
            h => h.ExecuteAsync(It.IsAny<DownloadItem>(), It.IsAny<DownloadJob>(),
                It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task ExecuteJobAsync_FileAlreadyExists_SkipsDownload()
    {
        // Arrange
        var destPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(destPath, "existing content");
        try
        {
            var job = CreateJob("https://ard.de/video.mp4", destPath);

            _validationServiceMock
                .Setup(s => s.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _downloadManager.ExecuteJobAsync(job, Mock.Of<IProgress<double>>(), CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            _fileDownloaderMock.As<IDownloadHandler>().Verify(
                h => h.ExecuteAsync(It.IsAny<DownloadItem>(), It.IsAny<DownloadJob>(),
                    It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }
        finally
        {
            File.Delete(destPath);
        }
    }
}

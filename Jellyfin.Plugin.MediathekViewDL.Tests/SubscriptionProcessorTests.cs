using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.Api.Converters;
using Jellyfin.Plugin.MediathekViewDL.Api.External;
using Jellyfin.Plugin.MediathekViewDL.Api.External.Models;
using Jellyfin.Plugin.MediathekViewDL.Api.Models;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;
using Jellyfin.Plugin.MediathekViewDL.Data;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Clients;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Models;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Queue;
using Jellyfin.Plugin.MediathekViewDL.Services.Library;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Jellyfin.Plugin.MediathekViewDL.Services.Subscriptions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests
{
    public class SubscriptionProcessorTests
    {
        private readonly Mock<ILogger<SubscriptionProcessor>> _loggerMock;
        private readonly Mock<IMediathekViewApiClient> _apiClientMock;
        private readonly Mock<IVideoParser> _videoParserMock;
        private readonly Mock<ILocalMediaScanner> _localMediaScannerMock;
        private readonly Mock<IFileNameBuilderService> _fileNameBuilderServiceMock;
        private readonly Mock<IStrmValidationService> _strmValidationServiceMock;
        private readonly Mock<IFFmpegService> _ffmpegServiceMock;
        private readonly Mock<IDownloadHistoryRepository> _downloadHistoryRepositoryMock;
        private readonly Mock<IConfigurationProvider> _configurationProviderMock;
        private readonly Mock<IDownloadQueueManager> _downloadQueueManagerMock;
        private readonly Mock<IOriginalVersionLanguageResolver> _originalVersionLanguageResolverMock;
        private readonly SubscriptionProcessor _processor;

        public SubscriptionProcessorTests()
        {
            _loggerMock = new Mock<ILogger<SubscriptionProcessor>>();
            _apiClientMock = new Mock<IMediathekViewApiClient>();
            _videoParserMock = new Mock<IVideoParser>();
            _localMediaScannerMock = new Mock<ILocalMediaScanner>();
            _fileNameBuilderServiceMock = new Mock<IFileNameBuilderService>();
            _strmValidationServiceMock = new Mock<IStrmValidationService>();
            _ffmpegServiceMock = new Mock<IFFmpegService>();
            _downloadHistoryRepositoryMock = new Mock<IDownloadHistoryRepository>();
            _configurationProviderMock = new Mock<IConfigurationProvider>();
            _downloadQueueManagerMock = new Mock<IDownloadQueueManager>();
            _originalVersionLanguageResolverMock = new Mock<IOriginalVersionLanguageResolver>();

            // Default setup: Validation always succeeds
            _strmValidationServiceMock
                .Setup(x => x.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            _configurationProviderMock
                .Setup(x => x.Configuration)
                .Returns(new PluginConfiguration());

            // Default setup: no original-version language could be resolved.
            _originalVersionLanguageResolverMock
                .Setup(x => x.TryGetOriginalVersionLanguageAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            _processor = new SubscriptionProcessor(
                _loggerMock.Object,
                _apiClientMock.Object,
                _videoParserMock.Object,
                _localMediaScannerMock.Object,
                _fileNameBuilderServiceMock.Object,
                _strmValidationServiceMock.Object,
                _ffmpegServiceMock.Object,
                _downloadHistoryRepositoryMock.Object,
                _configurationProviderMock.Object,
                _downloadQueueManagerMock.Object,
                _originalVersionLanguageResolverMock.Object
            );
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldReturnJob_WhenNewItemFound()
        {
            // Arrange
            var subscription = new Subscription { Name = "TestSub" };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle",
                UrlVideo = "http://test.com/video.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
            var job = jobs[0];
            Assert.Equal("123", job.ItemId);
            Assert.Equal("TestTitle", job.Title);
            Assert.Single(job.DownloadItems);
            Assert.Equal("http://test.com/video.mp4", job.DownloadItems.First().SourceUrl);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldNotDetectSecondaryAudio_WhenDisabled()
        {
            // Arrange
            var subscription = new Subscription { Name = "TestSub" };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle",
                UrlVideoHd = "http://test.com/video_sendeton_1080p.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mkv" });

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
            Assert.Single(jobs[0].DownloadItems);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldQueueDetectedSecondaryAudio_WhenEnabled()
        {
            // Arrange
            var subscription = new Subscription
            {
                Name = "TestSub",
                Download = new DownloadSettings
                {
                    DetectUndetectedSecondaryAudio = true,
                    DownloadOriginalVersionAudio = true,
                    DownloadAudioDescriptionAudio = true,
                    DownloadClearSpeechAudio = false,
                    CleanAudioTrackLabels = true,
                }
            };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle",
                UrlVideoHd = "http://test.com/video_sendeton_1080p.mp4",
                UrlWebsite = "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mkv" });

            _originalVersionLanguageResolverMock
                .Setup(x => x.TryGetOriginalVersionLanguageAsync(item.UrlWebsite, It.IsAny<CancellationToken>()))
                .ReturnsAsync("eng");

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
            var items = jobs[0].DownloadItems.ToList();

            // Main video + original-version + audio-description (clear-speech disabled) = 3 items.
            Assert.Equal(3, items.Count);

            // The original-version language resolver looked up the real language via the item's
            // website URL, instead of falling back to the URL-derived "und" placeholder.
            var originalVersion = items.Single(i => i.SourceUrl.Contains("_originalversion_", StringComparison.Ordinal));
            Assert.Equal("eng", originalVersion.Language);
            Assert.False(originalVersion.IsAudioDescription);
            Assert.Equal(DownloadType.AudioExtraction, originalVersion.JobType);

            var audioDescription = items.Single(i => i.SourceUrl.Contains("_audiodeskription_", StringComparison.Ordinal));
            Assert.Equal("deu", audioDescription.Language);
            Assert.True(audioDescription.IsAudioDescription);

            Assert.DoesNotContain(items, i => i.SourceUrl.Contains("_klaresprache_", StringComparison.Ordinal));

            // CleanAudioTrackLabels is enabled on the subscription, so every queued item - main video
            // and both detected secondary tracks - should carry it through.
            Assert.All(items, i => Assert.True(i.CleanAudioTrackLabel));
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldNotCleanAudioTrackLabels_WhenDisabled()
        {
            // Arrange
            var subscription = new Subscription { Name = "TestSub" };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle",
                UrlVideo = "http://test.com/video.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
            Assert.False(jobs[0].DownloadItems.Single().CleanAudioTrackLabel);
        }

        [Fact]
        public async Task GetEligibleItemsAsync_ShouldResolveRealLanguage_ForApiDetectedOriginalVersionItem()
        {
            // Arrange - simulates an item MediathekViewWeb already returns as its own search
            // result (not one derived from a main video URL), whose title the parser only
            // recognized as a generic original-version marker (e.g. "(OV)"), leaving Language "und".
            var subscription = new Subscription { Name = "TestSub" };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle (OV)",
                UrlVideo = "http://test.com/video.mp4",
                UrlWebsite = "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "und" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            _originalVersionLanguageResolverMock
                .Setup(x => x.TryGetOriginalVersionLanguageAsync(item.UrlWebsite, It.IsAny<CancellationToken>()))
                .ReturnsAsync("eng");

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
            Assert.Equal("eng", videoInfo.Language);
        }

        [Fact]
        public async Task GetEligibleItemsAsync_ShouldNotCallResolver_WhenResolveOriginalVersionLanguageDisabled()
        {
            // Arrange
            var subscription = new Subscription
            {
                Name = "TestSub",
                Download = new DownloadSettings { ResolveOriginalVersionLanguage = false }
            };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle (OV)",
                UrlVideo = "http://test.com/video.mp4",
                UrlWebsite = "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "und" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
            Assert.Equal("und", videoInfo.Language); // unchanged - resolver never called, no manual override configured
            _originalVersionLanguageResolverMock.Verify(
                x => x.TryGetOriginalVersionLanguageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldSkip_IfFoundLocally_AndEnhancedDetectionEnabled()
        {
            // Arrange
            var subscription = new Subscription
            {
                Name = "TestSub",
                Download = new DownloadSettings { EnhancedDuplicateDetection = true }
            };
            var item = new ResultItem { Id = "456", Title = "ExistingTitle" };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock.Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "ExistingTitle", Language = "deu" };
            _videoParserMock.Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock.Setup(x => x.GetSubscriptionBaseDirectory(It.IsAny<Subscription>(), It.IsAny<DownloadContext>()))
                .Returns("/tmp/TestSub");

            // Simulate local cache containing this item
            var localCache = new LocalEpisodeCache();
            // VideoInfo defaults: SeasonNumber=null, EpisodeNumber=null, AbsoluteEpisodeNumber=null
            // But we can force match by setting absolute number
            videoInfo.AbsoluteEpisodeNumber = 100;
            localCache.Add(null, null, 100, "path/to/file.mp4", "deu");

            _localMediaScannerMock.Setup(x => x.ScanDirectory("/tmp/TestSub", "TestSub"))
               .Returns(localCache);

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Empty(jobs);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldNotReAddHistory_IfFoundLocally_AndAlreadyRecorded()
        {
            // Arrange: an item that was already backfilled into history on a previous run (e.g. an
            // Audiodeskription track downloaded before "AllowAudioDescription" was turned off). A
            // subsequent subscription run - manual "Process" click, scheduled run, whatever - still
            // sees it via local duplicate detection and must not touch history again: doing so would
            // insert a fresh row with "now" as the timestamp, making the item jump back to the top of
            // "Download Verlauf" as if it had just been downloaded, even though nothing happened.
            var subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                Name = "TestSub",
                Download = new DownloadSettings { EnhancedDuplicateDetection = true }
            };
            var item = new ResultItem { Id = "456", Title = "ExistingTitle" };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock.Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "ExistingTitle", Language = "deu", AbsoluteEpisodeNumber = 100 };
            _videoParserMock.Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock.Setup(x => x.GetSubscriptionBaseDirectory(It.IsAny<Subscription>(), It.IsAny<DownloadContext>()))
                .Returns("/tmp/TestSub");

            var localCache = new LocalEpisodeCache();
            localCache.Add(null, null, 100, "path/to/file.mp4", "deu");
            _localMediaScannerMock.Setup(x => x.ScanDirectory("/tmp/TestSub", "TestSub"))
               .Returns(localCache);

            // Already backfilled by an earlier run.
            _downloadHistoryRepositoryMock
                .Setup(x => x.ExistsByItemIdAndSubscriptionIdAsync("456", subscription.Id))
                .ReturnsAsync(true);

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Empty(jobs);
            _downloadHistoryRepositoryMock.Verify(
                x => x.AddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldSkip_AudioDescription_IfDisabled()
        {
            // Arrange
            var subscription = new Subscription
            {
                Name = "TestSub",
                Accessibility = new AccessibilitySettings { AllowAudioDescription = false }
            };
            var item = new ResultItem { Id = "123", Title = "AD Content" };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };
            _apiClientMock.Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "AD Content", HasAudiodescription = true };
            _videoParserMock.Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Empty(jobs);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldSkip_RecognizedSeriesEpisode_WhenExcludeSeriesEnabled()
        {
            // Arrange
            var subscription = new Subscription
            {
                Name = "TestSub",
                Series = new SeriesSettings { ExcludeSeries = true }
            };
            var item = new ResultItem { Id = "123", Title = "Some Show S01E02" };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };
            _apiClientMock.Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "Some Show S01E02", IsShow = true };
            _videoParserMock.Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Empty(jobs);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldKeepItem_WhenExcludeSeriesEnabled_AndItemIsNotAShow()
        {
            // Arrange
            var subscription = new Subscription
            {
                Name = "TestSub",
                Series = new SeriesSettings { ExcludeSeries = true }
            };
            var item = new ResultItem
            {
                Id = "123",
                Title = "Some Movie",
                UrlVideo = "http://test.com/video.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };
            _apiClientMock.Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "Some Movie", IsShow = false };
            _videoParserMock.Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldCreateSubtitleJob_WhenEnabled()
        {
            // Arrange
            var subscription = new Subscription { Name = "TestSub" };
            var item = new ResultItem
            {
                Id = "123",
                UrlVideo = "http://video.mp4",
                UrlSubtitle = "http://subs.ttml"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };
            _apiClientMock.Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "Test", Language = "deu" };
            _videoParserMock.Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/v.mp4", SubtitleFilePath = "/tmp/s.ttml" });

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, true, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
            var job = jobs[0];
            Assert.Equal(2, job.DownloadItems.Count); // Video + Subtitle
            Assert.Contains(job.DownloadItems, d => d.JobType == DownloadType.SubtitleDownload && d.SourceUrl == "http://subs.ttml");
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldFallback_ToNextQuality_WhenPrimaryFails()
        {
            // Arrange
            var subscription = new Subscription
            {
                Name = "TestSub",
                Download = new DownloadSettings { QualityCheckWithUrl = true }
            };
            var item = new ResultItem
            {
                Id = "123",
                UrlVideoHd = "http://hd.mp4",
                UrlVideo = "http://sd.mp4",
                UrlVideoLow = "http://low.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock.Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "Test", Language = "deu" };
            _videoParserMock.Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/v.mp4" });
            _strmValidationServiceMock
                .Setup(x => x.ValidateUrlAsync("http://hd.mp4", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false); // Fail

            _strmValidationServiceMock
                .Setup(x => x.ValidateUrlAsync("http://sd.mp4", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true); // Success

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
            var job = jobs[0];
            Assert.Equal("http://sd.mp4", job.DownloadItems.First().SourceUrl);

            // Verify HD was checked first
            _strmValidationServiceMock.Verify(x => x.ValidateUrlAsync("http://hd.mp4", It.IsAny<CancellationToken>()), Times.Once);
            _strmValidationServiceMock.Verify(x => x.ValidateUrlAsync("http://sd.mp4", It.IsAny<CancellationToken>()), Times.Once);
            _strmValidationServiceMock.Verify(x => x.ValidateUrlAsync("http://low.mp4", It.IsAny<CancellationToken>()), Times.Never);

            // The item still carries the other known quality URLs as execution-time fallbacks, in
            // case the chosen "http://sd.mp4" has since expired by the time the queue gets to it
            // (see DownloadItem.FallbackSourceUrls).
            Assert.Contains("http://low.mp4", job.DownloadItems.First().FallbackSourceUrls!);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldSkip_WhenAllQualitiesFail()
        {
            // Arrange
            var subscription = new Subscription
            {
                Name = "TestSub",
                Download = new DownloadSettings { QualityCheckWithUrl = true }
            };
            var item = new ResultItem
            {
                Id = "123",
                UrlVideoHd = "http://hd.mp4",
                UrlVideo = "http://sd.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock.Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "Test", Language = "deu" };
            _videoParserMock.Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/v.mp4" });

            // Fail all
            _strmValidationServiceMock
                .Setup(x => x.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Empty(jobs); // Should not create a job
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldSkip_IfFoundInHistoryByUrl_AndItemIdChanged()
        {
            // Arrange
            // The API re-published the same video under a new item ID, but the video URL is identical.
            // The download history must still detect the duplicate by URL to avoid re-downloading.
            var subscription = new Subscription { Id = Guid.NewGuid(), Name = "TestSub" };
            var item = new ResultItem
            {
                Id = "new-id",
                Title = "TestTitle",
                UrlVideo = "http://test.com/video.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            _downloadHistoryRepositoryMock
                .Setup(x => x.ExistsByItemIdAndSubscriptionIdAsync("new-id", subscription.Id))
                .ReturnsAsync(false);
            _downloadHistoryRepositoryMock
                .Setup(x => x.ExistsByAnyUrlAndSubscriptionIdAsync(It.IsAny<IEnumerable<string>>(), subscription.Id))
                .ReturnsAsync(true);

            var config = new PluginConfiguration();
            config.Subscriptions.Add(subscription);
            _configurationProviderMock.Setup(x => x.ConfigurationOrNull).Returns(config);
            _configurationProviderMock.Setup(x => x.Configuration).Returns(config);

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Empty(jobs);
        }

        [Fact]
        public async Task ProcessSubscriptionAsync_ShouldQueueJobsAndUpdateTimestamp()
        {
            // Arrange
            var subscription = new Subscription { Id = Guid.NewGuid(), Name = "TestSub" };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle",
                UrlVideo = "http://test.com/video.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            var config = new PluginConfiguration();
            config.Subscriptions.Add(subscription);
            _configurationProviderMock.Setup(x => x.ConfigurationOrNull).Returns(config);
            _configurationProviderMock.Setup(x => x.Configuration).Returns(config);

            // Act
            var count = await _processor.ProcessSubscriptionAsync(subscription, CancellationToken.None);

            // Assert
            Assert.Equal(1, count);
            _downloadQueueManagerMock.Verify(x => x.QueueJob(It.IsAny<DownloadJob>(), subscription.Id), Times.Once);
            Assert.NotEqual(default, subscription.LastDownloadedTimestamp);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldSkipItem_WhenRequiredAudioLanguageIsNotFound()
        {
            // Arrange: RequiredAudioLanguage is set to "eng", but neither secondary-audio detection
            // is enabled nor does the main (German) track itself match - so the item has no way to
            // end up with an English track and must be skipped entirely.
            var subscription = new Subscription
            {
                Name = "TestSub",
                Accessibility = new AccessibilitySettings { RequiredAudioLanguage = "eng" }
            };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle",
                UrlVideo = "http://test.com/video.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Empty(jobs);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldKeepItem_WhenMainTrackAlreadyMatchesRequiredAudioLanguage()
        {
            // Arrange: MediathekView itself already returned this item as the English-language
            // track (e.g. a distinct OV search result), so the main track alone already satisfies
            // the filter without any secondary-audio detection.
            var subscription = new Subscription
            {
                Name = "TestSub",
                Accessibility = new AccessibilitySettings { RequiredAudioLanguage = "eng" }
            };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle (Originalversion)",
                UrlVideo = "http://test.com/video.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "eng" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
        }

        [Fact]
        public async Task GetJobsForSubscriptionAsync_ShouldKeepItem_WhenRequiredAudioLanguageIsFoundViaWorkaroundDetection()
        {
            // Arrange: the main track is German, but our own URL-based secondary-audio detection
            // (SecondaryAudioUrlHelper) finds and resolves an English original-version track for it -
            // that alone must be enough to satisfy the filter, even though MediathekView's own search
            // index never surfaced the English track as a distinct result.
            var subscription = new Subscription
            {
                Name = "TestSub",
                Accessibility = new AccessibilitySettings { RequiredAudioLanguage = "eng" },
                Download = new DownloadSettings
                {
                    DetectUndetectedSecondaryAudio = true,
                    DownloadOriginalVersionAudio = true,
                    ResolveOriginalVersionLanguage = true,
                }
            };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle",
                UrlVideoHd = "http://test.com/video_sendeton_1080p.mp4",
                UrlWebsite = "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mkv" });

            _originalVersionLanguageResolverMock
                .Setup(x => x.TryGetOriginalVersionLanguageAsync(item.UrlWebsite, It.IsAny<CancellationToken>()))
                .ReturnsAsync("eng");

            // Act
            var jobs = await _processor.GetJobsForSubscriptionAsync(subscription, false, CancellationToken.None);

            // Assert
            Assert.Single(jobs);
            Assert.Contains(jobs[0].DownloadItems, i => i.Language == "eng");
        }

        [Fact]
        public async Task TestSubscriptionAsync_ShouldExcludeItem_WhenRequiredAudioLanguageIsNotFound()
        {
            // Arrange: mirrors GetJobsForSubscriptionAsync_ShouldSkipItem_WhenRequiredAudioLanguageIsNotFound -
            // the dry-run preview must agree with what a real run would actually download.
            var subscription = new Subscription
            {
                Name = "TestSub",
                Accessibility = new AccessibilitySettings { RequiredAudioLanguage = "eng" }
            };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle",
                UrlVideo = "http://test.com/video.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            // Act
            var results = new List<ResultItemDto>();
            await foreach (var result in _processor.TestSubscriptionAsync(subscription, CancellationToken.None))
            {
                results.Add(result);
            }

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public async Task TestSubscriptionAsync_ShouldIncludeItem_WhenMainTrackAlreadyMatchesRequiredAudioLanguage()
        {
            // Arrange
            var subscription = new Subscription
            {
                Name = "TestSub",
                Accessibility = new AccessibilitySettings { RequiredAudioLanguage = "eng" }
            };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle (Originalversion)",
                UrlVideo = "http://test.com/video.mp4"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "eng" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mp4" });

            // Act
            var results = new List<ResultItemDto>();
            await foreach (var result in _processor.TestSubscriptionAsync(subscription, CancellationToken.None))
            {
                results.Add(result);
            }

            // Assert
            Assert.Single(results);
        }

        [Fact]
        public async Task TestSubscriptionAsync_ShouldIncludeItem_WhenRequiredAudioLanguageIsFoundViaWorkaroundDetection()
        {
            // Arrange: mirrors GetJobsForSubscriptionAsync_ShouldKeepItem_WhenRequiredAudioLanguageIsFoundViaWorkaroundDetection -
            // the dry-run preview must find the same URL-derived secondary track a real run would.
            var subscription = new Subscription
            {
                Name = "TestSub",
                Accessibility = new AccessibilitySettings { RequiredAudioLanguage = "eng" },
                Download = new DownloadSettings
                {
                    DetectUndetectedSecondaryAudio = true,
                    DownloadOriginalVersionAudio = true,
                    ResolveOriginalVersionLanguage = true,
                }
            };
            var item = new ResultItem
            {
                Id = "123",
                Title = "TestTitle",
                UrlVideoHd = "http://test.com/video_sendeton_1080p.mp4",
                UrlWebsite = "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw"
            };

            var resultChannels = new ResultChannels
            {
                Results = new Collection<ResultItem> { item },
                QueryInfo = new QueryInfo { TotalResults = 1 }
            };

            _apiClientMock
                .Setup(x => x.SearchAsync(It.IsAny<ApiQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultChannels.ToDto(new ApiQueryDto(), false));

            var videoInfo = new VideoInfo { Title = "TestTitle", Language = "deu" };
            _videoParserMock
                .Setup(x => x.ParseVideoInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(videoInfo);

            _fileNameBuilderServiceMock
                .Setup(x => x.GenerateDownloadPaths(It.IsAny<VideoInfo>(), It.IsAny<Subscription>(), It.IsAny<DownloadContext>(), It.IsAny<FileType?>()))
                .Returns(new DownloadPaths { DirectoryPath = "/tmp", MainFilePath = "/tmp/video.mkv" });

            _originalVersionLanguageResolverMock
                .Setup(x => x.TryGetOriginalVersionLanguageAsync(item.UrlWebsite, It.IsAny<CancellationToken>()))
                .ReturnsAsync("eng");

            // Act
            var results = new List<ResultItemDto>();
            await foreach (var result in _processor.TestSubscriptionAsync(subscription, CancellationToken.None))
            {
                results.Add(result);
            }

            // Assert
            Assert.Single(results);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.Api.External;
using Jellyfin.Plugin.MediathekViewDL.Api.Models;
using Jellyfin.Plugin.MediathekViewDL.Api.Models.Enums;
using Jellyfin.Plugin.MediathekViewDL.Configuration;
using Jellyfin.Plugin.MediathekViewDL.Configuration.SubscriptionSettings;
using Jellyfin.Plugin.MediathekViewDL.Data;
using Jellyfin.Plugin.MediathekViewDL.Exceptions.ExternalApi;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Clients;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Models;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Queue;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Jellyfin.Plugin.MediathekViewDL.Services.Metadata;
using Jellyfin.Plugin.MediathekViewDL.Services.Subscriptions;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Api.Controller;

/// <summary>
/// The controller for managing downloads.
/// </summary>
[ApiController]
[Route("MediathekViewDL/[controller]")]
[Authorize(Policy = Policies.RequiresElevation)]
public class DownloadsController : ControllerBase
{
    private readonly IDownloadQueueManager _downloadQueueManager;
    private readonly IDownloadHistoryRepository _downloadHistoryRepository;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IVideoParser _videoParser;
    private readonly IFileNameBuilderService _fileNameBuilder;
    private readonly ILogger<DownloadsController> _logger;
    private readonly IOriginalVersionLanguageResolver _originalVersionLanguageResolver;
    private readonly IMediathekViewApiClient _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadsController"/> class.
    /// </summary>
    /// <param name="downloadQueueManager">The download queue manager.</param>
    /// <param name="downloadHistoryRepository">The download history repository.</param>
    /// <param name="configurationProvider">The configuration provider.</param>
    /// <param name="videoParser">The video parser.</param>
    /// <param name="fileNameBuilder">The file name builder service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="originalVersionLanguageResolver">Resolves the correct original-version language code from the relevant broadcaster's own API.</param>
    /// <param name="apiClient">The MediathekView API client, used to look up sibling audio-variant results.</param>
    public DownloadsController(
        IDownloadQueueManager downloadQueueManager,
        IDownloadHistoryRepository downloadHistoryRepository,
        IConfigurationProvider configurationProvider,
        IVideoParser videoParser,
        IFileNameBuilderService fileNameBuilder,
        ILogger<DownloadsController> logger,
        IOriginalVersionLanguageResolver originalVersionLanguageResolver,
        IMediathekViewApiClient apiClient)
    {
        _downloadQueueManager = downloadQueueManager;
        _downloadHistoryRepository = downloadHistoryRepository;
        _configurationProvider = configurationProvider;
        _videoParser = videoParser;
        _fileNameBuilder = fileNameBuilder;
        _logger = logger;
        _originalVersionLanguageResolver = originalVersionLanguageResolver;
        _apiClient = apiClient;
    }

    /// <summary>
    /// Gets the currently active downloads.
    /// </summary>
    /// <returns>A list of active downloads.</returns>
    [HttpGet("Active")]
    public ActionResult<IEnumerable<ActiveDownload>> GetActiveDownloads()
    {
        if (Plugin.Instance?.InitializationException is not null)
        {
            return StatusCode(503, new ApiErrorDto(ApiErrorId.InitializationError, Plugin.Instance.InitializationException.Message));
        }

        return Ok(_downloadQueueManager.GetActiveDownloads());
    }

    /// <summary>
    /// Gets the download history.
    /// </summary>
    /// <param name="limit">The maximum number of entries to return.</param>
    /// <returns>A list of download history entries.</returns>
    [HttpGet("History")]
    public async Task<ActionResult<IEnumerable<DownloadHistoryEntry>>> GetDownloadHistory([FromQuery] int limit = 50)
    {
        if (Plugin.Instance?.InitializationException is not null)
        {
            return StatusCode(503, new ApiErrorDto(ApiErrorId.InitializationError, Plugin.Instance.InitializationException.Message));
        }

        var history = await _downloadHistoryRepository.GetRecentHistoryAsync(limit).ConfigureAwait(false);
        return Ok(history);
    }

    /// <summary>
    /// Gets the grouped download history.
    /// </summary>
    /// <param name="limit">The maximum number of raw entries to fetch before grouping.</param>
    /// <returns>A list of grouped download history entries.</returns>
    [HttpGet("History/Grouped")]
    public async Task<ActionResult<IEnumerable<GroupedDownloadHistoryDto>>> GetGroupedDownloadHistory([FromQuery] int limit = 100)
    {
        if (Plugin.Instance?.InitializationException is not null)
        {
            return StatusCode(503, new ApiErrorDto(ApiErrorId.InitializationError, Plugin.Instance.InitializationException.Message));
        }

        var history = await _downloadHistoryRepository.GetRecentHistoryAsync(limit).ConfigureAwait(false);
        var groups = new List<GroupedDownloadHistoryDto>();

        foreach (var entry in history)
        {
            var entrySubId = entry.SubscriptionId;
            var entryItemId = entry.ItemId;
            var entryTitle = entry.Title;
            var entryFileName = !string.IsNullOrEmpty(entry.DownloadPath) ? System.IO.Path.GetFileName(entry.DownloadPath) : string.Empty;
            var entryDisplayName = !string.IsNullOrWhiteSpace(entryTitle) ? entryTitle : entryFileName;

            var group = groups.Find(g =>
            {
                if (g.SubscriptionId != entrySubId)
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(entryItemId) && !string.IsNullOrEmpty(g.ItemId) && entryItemId == g.ItemId)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(entryTitle) && !string.IsNullOrEmpty(g.Title) && entryTitle == g.Title)
                {
                    return true;
                }

                return !string.IsNullOrEmpty(entryDisplayName) && !string.IsNullOrEmpty(g.DisplayName) && entryDisplayName == g.DisplayName;
            });

            if (group == null)
            {
                group = new GroupedDownloadHistoryDto
                {
                    SubscriptionId = entrySubId,
                    Title = entryTitle,
                    DisplayName = entryDisplayName,
                    ItemId = entryItemId,
                    LatestTimestamp = entry.Timestamp
                };
                groups.Add(group);
            }

            group.Entries.Add(entry);

            if (!string.IsNullOrEmpty(entryDisplayName) && (string.IsNullOrEmpty(group.DisplayName) || entryDisplayName.Length < group.DisplayName.Length))
            {
                group.DisplayName = entryDisplayName;
            }

            if (entry.Timestamp > group.LatestTimestamp)
            {
                group.LatestTimestamp = entry.Timestamp;
            }
        }

        return Ok(groups.OrderByDescending(g => g.LatestTimestamp));
    }

    /// <summary>
    /// Cancels a specific download.
    /// </summary>
    /// <param name="id">The active download identifier.</param>
    /// <returns>An OK result.</returns>
    [HttpDelete("{id}")]
    public IActionResult CancelDownload([FromRoute] Guid id)
    {
        try
        {
            _downloadQueueManager.CancelJob(id);
            return Ok($"Download '{id}' Abbruch angefordert.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorDto(ApiErrorId.NotFound, $"Download mit ID '{id}' wurde nicht gefunden."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorDto(ApiErrorId.InvalidOperation, ex.Message));
        }
    }

    /// <summary>
    /// Cancels all active downloads.
    /// </summary>
    /// <returns>An OK result.</returns>
    [HttpDelete]
    public IActionResult CancelAllDownloads()
    {
        _downloadQueueManager.CancelAllJobs();
        return Ok("Abbruch aller Downloads angefordert.");
    }

    /// <summary>
    /// Clears all finished, failed or cancelled downloads from the active list.
    /// </summary>
    /// <returns>An OK result.</returns>
    [HttpPost("ClearInactive")]
    public IActionResult ClearInactiveDownloads()
    {
        _downloadQueueManager.ClearInactiveJobs();
        return Ok("Inaktive Downloads aus der Liste entfernt.");
    }

    /// <summary>
    /// Triggers a download for a single item.
    /// </summary>
    /// <param name="item">The item to download.</param>
    /// <returns>An OK result.</returns>
    [HttpPost]
    public async Task<IActionResult> Download([FromBody] ResultItemDto? item)
    {
        if (Plugin.Instance?.InitializationException is not null)
        {
            return StatusCode(503, new ApiErrorDto(ApiErrorId.InitializationError, Plugin.Instance.InitializationException.Message));
        }

        var config = _configurationProvider.ConfigurationOrNull;
        if (config == null)
        {
            _logger.LogError("Plugin configuration is not available. Cannot start manual download.");
            return StatusCode(500, new ApiErrorDto(ApiErrorId.ConfigurationNotAvailable, "Plugin-Konfiguration ist nicht verfügbar."));
        }

        var videoUrl = item?.GetVideoByQuality()?.Url;

        if (item == null || string.IsNullOrWhiteSpace(videoUrl))
        {
            return BadRequest(new ApiErrorDto(ApiErrorId.InvalidItem, "Ungültiges Element für den Download bereitgestellt (keine Video-URL)."));
        }

        var videoInfo = _videoParser.ParseVideoInfo(item.Topic, item.Title, item.Channel);
        if (videoInfo == null)
        {
            _logger.LogError("Could not parse video info for item: {Title}", item.Title);
            return BadRequest(new ApiErrorDto(ApiErrorId.ParseError, "Video-Informationen konnten nicht analysiert werden."));
        }

        var defaultSub = new Subscription() { Name = item.Topic };
        var paths = _fileNameBuilder.GenerateDownloadPaths(videoInfo, defaultSub, DownloadContext.Manual, FileType.Video);

        if (!paths.IsValid)
        {
            _logger.LogError("Could not generate download paths for item: {Title}", item.Title);
            return BadRequest(new ApiErrorDto(ApiErrorId.InvalidPath, "Download-Pfade konnten nicht generiert werden."));
        }

        if (FileDownloader.GetDiskSpace(paths.DirectoryPath) < config.Download.MinFreeDiskSpaceBytes)
        {
            _logger.LogError("Not enough free disk space to start download for item: {Title} at {Path}", item.Title, paths.DirectoryPath);
            return BadRequest(new ApiErrorDto(ApiErrorId.InsufficientDiskSpace, "Nicht genügend freier Speicherplatz, um den Download zu starten."));
        }

        _logger.LogInformation("Manual download requested for item: {Title}", item.Title);

        var subtitle = item.GetSubtitle();
        var subtitleUrl = (config.Download.DownloadSubtitles && !string.IsNullOrWhiteSpace(subtitle?.Url)) ? subtitle!.Url : null;

        var job = new DownloadJob
        {
            ItemId = item.Id,
            Title = item.Title,
            ItemInfo = videoInfo,
            MediaMetadata = MediaMetadataFactory.Create(item, videoUrl, subtitleUrl, videoInfo),
        };

        job.DownloadItems.Add(new DownloadItem { SourceUrl = videoUrl, DestinationPath = paths.MainFilePath, JobType = DownloadType.FFmpegDownload, CleanAudioTrackLabel = config.SubscriptionDefaults.DownloadSettings.CleanAudioTrackLabels });

        await AddDetectedSecondaryAudioItemsAsync(job, item, videoUrl, paths.MainFilePath, config.SubscriptionDefaults.DownloadSettings).ConfigureAwait(false);
        await AddCrossResultAudioVariantItemsAsync(job, item, videoInfo, paths.MainFilePath, config.SubscriptionDefaults.DownloadSettings, HttpContext.RequestAborted).ConfigureAwait(false);

        if (subtitleUrl is not null)
        {
            job.DownloadItems.Add(new DownloadItem { SourceUrl = subtitleUrl, DestinationPath = paths.SubtitleFilePath, JobType = DownloadType.SubtitleDownload });
        }

        _downloadQueueManager.QueueJob(job);
        return Ok($"Download für '{item.Title}' in Warteschlange.");
    }

    /// <summary>
    /// Triggers an advanced download for a single item with custom options.
    /// </summary>
    /// <param name="options">The advanced download options.</param>
    /// <returns>An OK result.</returns>
    [HttpPost("Advanced")]
    public async Task<IActionResult> AdvancedDownload([FromBody] AdvancedDownloadOptions? options)
    {
        if (Plugin.Instance?.InitializationException is not null)
        {
            return StatusCode(503, new ApiErrorDto(ApiErrorId.InitializationError, Plugin.Instance.InitializationException.Message));
        }

        var config = _configurationProvider.ConfigurationOrNull;
        if (config == null)
        {
            _logger.LogError("Plugin configuration is not available. Cannot start advanced download.");
            return StatusCode(500, new ApiErrorDto(ApiErrorId.ConfigurationNotAvailable, "Plugin-Konfiguration ist nicht verfügbar."));
        }

        if (options == null)
        {
            return BadRequest(new ApiErrorDto(ApiErrorId.InvalidOptions, "Erweiterte Download-Optionen sind erforderlich."));
        }

        var item = options.Item;
        var videoUrl = item.GetVideoByQuality()?.Url;

        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            return BadRequest(new ApiErrorDto(ApiErrorId.InvalidItem, "Ungültiges Element für den Download bereitgestellt (keine Video-URL)."));
        }

        if (string.IsNullOrWhiteSpace(options.DownloadPath) || string.IsNullOrWhiteSpace(options.FileName))
        {
            return BadRequest(new ApiErrorDto(ApiErrorId.InvalidOptions, "Download-Pfad und Dateiname sind für den erweiterten Download erforderlich."));
        }

        if (!_fileNameBuilder.IsPathSafe(options.DownloadPath))
        {
            _logger.LogWarning("Blocked advanced download request to unsafe path: {Path}", options.DownloadPath);
            return BadRequest(new ApiErrorDto(ApiErrorId.UnsafePath, "Der angegebene Download-Pfad ist nicht zulässig. Bitte verwenden Sie einen Pfad innerhalb Ihrer Bibliothek oder der konfigurierten Download-Verzeichnisse."));
        }

        if (_fileNameBuilder.SanitizeFileName(options.FileName) != options.FileName)
        {
            return BadRequest(new ApiErrorDto(ApiErrorId.InvalidFilename, "Der Dateiname enthält ungültige Zeichen."));
        }

        var videoInfo = _videoParser.ParseVideoInfo(item.Topic, item.Title, item.Channel);
        if (videoInfo == null)
        {
            _logger.LogError("Could not parse video info for item: {Title}", item.Title);
            return BadRequest(new ApiErrorDto(ApiErrorId.ParseError, "Video-Informationen konnten nicht analysiert werden."));
        }

#pragma warning disable CA3003 // Path is validated via manual check and directory creation rules
        if (FileDownloader.GetDiskSpace(options.DownloadPath) < config.Download.MinFreeDiskSpaceBytes)
#pragma warning restore CA3003
        {
            _logger.LogError("Not enough free disk space to start advanced download for item: {Title} at {Path}", item.Title, options.DownloadPath);
            return BadRequest(new ApiErrorDto(ApiErrorId.InsufficientDiskSpace, "Nicht genügend freier Speicherplatz, um den Download zu starten."));
        }

        _logger.LogInformation("Advanced download requested for item: {Title} to path: {Path} with filename: {FileName}", item.Title, options.DownloadPath, options.FileName);

        var videoDestinationPath = Path.Combine(options.DownloadPath, _fileNameBuilder.SanitizeFileName(options.FileName));
        var subtitle = item.GetSubtitle();
        var subtitleUrl = (options.DownloadSubtitles && !string.IsNullOrWhiteSpace(subtitle?.Url)) ? subtitle!.Url : null;

        var job = new DownloadJob
        {
            ItemId = item.Id,
            Title = item.Title,
            ItemInfo = videoInfo,
            MediaMetadata = MediaMetadataFactory.Create(item, videoUrl, subtitleUrl, videoInfo),
        };

        job.DownloadItems.Add(new DownloadItem { SourceUrl = videoUrl, DestinationPath = videoDestinationPath, JobType = DownloadType.FFmpegDownload, CleanAudioTrackLabel = config.SubscriptionDefaults.DownloadSettings.CleanAudioTrackLabels });

        if (!string.IsNullOrWhiteSpace(options.SecondaryAudioUrl))
        {
            // Explicit manual override: a single standalone track, exactly as the user specified.
            var manualLang = string.IsNullOrWhiteSpace(options.SecondaryAudioLanguage) ? "und" : options.SecondaryAudioLanguage;
            job.DownloadItems.Add(new DownloadItem
            {
                SourceUrl = options.SecondaryAudioUrl,
                DestinationPath = Path.ChangeExtension(videoDestinationPath, null) + "." + manualLang + ".mka",
                Language = manualLang,
                CleanAudioTrackLabel = config.SubscriptionDefaults.DownloadSettings.CleanAudioTrackLabels,
                JobType = DownloadType.AudioExtraction
            });
        }
        else
        {
            await AddDetectedSecondaryAudioItemsAsync(job, item, videoUrl, videoDestinationPath, config.SubscriptionDefaults.DownloadSettings).ConfigureAwait(false);
            await AddCrossResultAudioVariantItemsAsync(job, item, videoInfo, videoDestinationPath, config.SubscriptionDefaults.DownloadSettings, HttpContext.RequestAborted).ConfigureAwait(false);
        }

        if (subtitleUrl is not null)
        {
            string subtitleFileName;
            if (!string.IsNullOrWhiteSpace(options.SubtitleName))
            {
                subtitleFileName = _fileNameBuilder.SanitizeFileName(options.SubtitleName);
            }
            else
            {
                var defaultSub = new Subscription() { Name = item.Topic };
                var genPaths = _fileNameBuilder.GenerateDownloadPaths(videoInfo, defaultSub, DownloadContext.Manual, FileType.Video);
                subtitleFileName = Path.GetFileName(genPaths.SubtitleFilePath);
            }

            var subtitleDestinationPath = Path.Combine(options.DownloadPath, subtitleFileName);
            job.DownloadItems.Add(new DownloadItem { SourceUrl = subtitleUrl, DestinationPath = subtitleDestinationPath, JobType = DownloadType.SubtitleDownload });
        }

        _downloadQueueManager.QueueJob(job);
        return Ok($"Advanced download for '{item.Title}' queued.");
    }

    /// <summary>
    /// Detects and queues any secondary audio tracks (original version, audio description, "klare
    /// Sprache") that MediathekViewWeb's search index doesn't surface as a separate result, derived
    /// directly from the main video's URL. Shared by <see cref="Download"/> and
    /// <see cref="AdvancedDownload"/> so the two can't silently drift apart.
    /// </summary>
    /// <param name="job">The download job to add the detected items to.</param>
    /// <param name="item">The item being downloaded, used to resolve the original-version language.</param>
    /// <param name="videoUrl">The resolved main video URL.</param>
    /// <param name="mainFilePath">The destination path of the main video file.</param>
    /// <param name="downloadSettings">The download settings that decide which kinds are enabled.</param>
    /// <returns>A task that completes once all detected items have been queued.</returns>
    private async Task AddDetectedSecondaryAudioItemsAsync(
        DownloadJob job,
        ResultItemDto item,
        string videoUrl,
        string mainFilePath,
        BaseDownloadSettings downloadSettings)
    {
        foreach (var candidate in SecondaryAudioUrlHelper.DetectCandidates(videoUrl))
        {
            if (!SecondaryAudioUrlHelper.IsKindEnabled(downloadSettings, candidate.Kind))
            {
                continue;
            }

            var isOriginalVersion = candidate.Kind == SecondaryAudioKind.OriginalVersion;
            string? candidateLang;
            if (isOriginalVersion && downloadSettings.ResolveOriginalVersionLanguage)
            {
                _logger.LogInformation("Resolving original-version language for '{Title}' using UrlWebsite '{UrlWebsite}'.", item.Title, item.UrlWebsite ?? "(null)");
                candidateLang = (await _originalVersionLanguageResolver.TryGetOriginalVersionLanguageAsync(item.UrlWebsite, HttpContext.RequestAborted).ConfigureAwait(false)) ?? candidate.LanguageCode;
            }
            else
            {
                candidateLang = candidate.LanguageCode;
            }

            // Standalone file next to the main video (e.g. "Title.eng.mka"), same naming convention as a
            // secondary-language item found via the API - self-contained, no dependency on any other job.
            var kindTag = candidate.Kind == SecondaryAudioKind.AudioDescription ? " [AD]" : string.Empty;
            var standaloneDestination = Path.ChangeExtension(mainFilePath, null) + kindTag + "." + candidateLang + ".mka";

            job.DownloadItems.Add(new DownloadItem
            {
                SourceUrl = candidate.Url,
                DestinationPath = standaloneDestination,
                Language = candidateLang,
                IsAudioDescription = candidate.Kind == SecondaryAudioKind.AudioDescription,
                CleanAudioTrackLabel = downloadSettings.CleanAudioTrackLabels,
                JobType = DownloadType.AudioExtraction
            });
        }
    }

    /// <summary>
    /// Looks up other MediathekViewWeb search results for the same topic, groups them against the
    /// item being manually downloaded via <see cref="AudioVariantGroupingService"/>, and queues any
    /// sibling found to represent the same episode with a different audio track (e.g. arte's
    /// "ARTE.DE"/"ARTE.FR" channel split, ZDF/ZDFneo/3sat's per-language rows) as a standalone
    /// secondary-audio file next to the main video - the manual-download counterpart of
    /// <see cref="SubscriptionProcessor"/>'s subscription-time grouping, since a single manual
    /// download has no pre-fetched result list of its own to group against.
    /// </summary>
    /// <param name="job">The download job to add grouped-in items to.</param>
    /// <param name="item">The item being downloaded.</param>
    /// <param name="videoInfo">The parsed video info for <paramref name="item"/>.</param>
    /// <param name="mainFilePath">The destination path of the main video file.</param>
    /// <param name="downloadSettings">The download settings that decide whether this is enabled and which kinds are allowed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once all grouped-in items have been queued.</returns>
    private async Task AddCrossResultAudioVariantItemsAsync(
        DownloadJob job,
        ResultItemDto item,
        VideoInfo videoInfo,
        string mainFilePath,
        BaseDownloadSettings downloadSettings,
        CancellationToken cancellationToken)
    {
        if (!downloadSettings.DetectCrossResultAudioVariants || string.IsNullOrWhiteSpace(item.Topic))
        {
            return;
        }

        var query = new ApiQueryDto
        {
            Queries = new Collection<QueryFieldsDto>
            {
                new() { Fields = new Collection<QueryFieldType> { QueryFieldType.Topic }, Query = item.Topic }
            },
            Size = 50,
            MinBroadcastDate = item.Timestamp.AddHours(-48),
            MaxBroadcastDate = item.Timestamp.AddHours(48),
        };

        QueryResultDto result;
        try
        {
            result = await _apiClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (MediathekException ex)
        {
            _logger.LogWarning(ex, "Could not look up sibling audio-variant results for '{Title}'.", item.Title);
            return;
        }

        var candidates = new List<(ResultItemDto Item, VideoInfo VideoInfo)> { (item, videoInfo) };
        foreach (var candidateItem in result.Results)
        {
            if (candidateItem.Id == item.Id)
            {
                continue;
            }

            var candidateInfo = _videoParser.ParseVideoInfo(item.Topic, candidateItem.Title, candidateItem.Channel);
            if (candidateInfo == null)
            {
                continue;
            }

            if (candidateInfo.Language == "und" && downloadSettings.ResolveOriginalVersionLanguage)
            {
                candidateInfo.Language = (await _originalVersionLanguageResolver
                    .TryGetOriginalVersionLanguageAsync(candidateItem.UrlWebsite, cancellationToken)
                    .ConfigureAwait(false)) ?? candidateInfo.Language;
            }

            candidates.Add((candidateItem, candidateInfo));
        }

        var group = AudioVariantGroupingService.GroupByEpisode(candidates)
            .FirstOrDefault(g => g.MainItem.Id == item.Id || g.Secondaries.Any(s => s.Item.Id == item.Id));
        if (group == null || group.Secondaries.Count == 0)
        {
            return;
        }

        foreach (var secondary in group.Secondaries)
        {
            // The item being manually downloaded might itself have been grouped as a secondary of a
            // different sibling (e.g. the user picked the arte OV row directly) - only add the *other*
            // siblings, never re-add the item that's already the job's own main track.
            if (secondary.Item.Id == item.Id)
            {
                continue;
            }

            if (!SecondaryAudioUrlHelper.IsKindEnabled(downloadSettings, secondary.Kind))
            {
                continue;
            }

            var secondaryUrl = secondary.Item.GetVideoByQuality()?.Url;
            if (string.IsNullOrWhiteSpace(secondaryUrl))
            {
                _logger.LogWarning("Could not resolve a video URL for grouped audio-variant sibling '{Title}' (ID: {Id}); skipping this track.", secondary.Item.Title, secondary.Item.Id);
                continue;
            }

            var lang = string.IsNullOrWhiteSpace(secondary.VideoInfo.Language) ? "und" : secondary.VideoInfo.Language;
            var kindTag = secondary.Kind == SecondaryAudioKind.AudioDescription ? " [AD]" : string.Empty;
            var standaloneDestination = Path.ChangeExtension(mainFilePath, null) + kindTag + "." + lang + ".mka";

            job.DownloadItems.Add(new DownloadItem
            {
                SourceUrl = secondaryUrl,
                DestinationPath = standaloneDestination,
                Language = lang,
                IsAudioDescription = secondary.Kind == SecondaryAudioKind.AudioDescription,
                CleanAudioTrackLabel = downloadSettings.CleanAudioTrackLabels,
                JobType = DownloadType.AudioExtraction,
                SourceItemId = secondary.Item.Id
            });
        }
    }
}

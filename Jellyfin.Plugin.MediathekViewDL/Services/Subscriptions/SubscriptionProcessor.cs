using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.Api.External;
using Jellyfin.Plugin.MediathekViewDL.Api.Models;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;
using Jellyfin.Plugin.MediathekViewDL.Data;
using Jellyfin.Plugin.MediathekViewDL.Exceptions.ExternalApi;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Clients;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Models;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Queue;
using Jellyfin.Plugin.MediathekViewDL.Services.Library;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Jellyfin.Plugin.MediathekViewDL.Services.Metadata;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Subscriptions;

/// <summary>
/// Service responsible for searching and filtering content for subscriptions.
/// </summary>
public class SubscriptionProcessor : ISubscriptionProcessor
{
    private readonly ILogger<SubscriptionProcessor> _logger;
    private readonly IMediathekViewApiClient _apiClient;
    private readonly IVideoParser _videoParser;
    private readonly ILocalMediaScanner _localMediaScanner;
    private readonly IFileNameBuilderService _fileNameBuilderService;
    private readonly IStrmValidationService _strmValidationService;
    private readonly IFFmpegService _ffmpegService;
    private readonly IDownloadHistoryRepository _downloadHistoryRepository;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IDownloadQueueManager _downloadQueueManager;
    private readonly IOriginalVersionLanguageResolver _originalVersionLanguageResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionProcessor"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="apiClient">The API client.</param>
    /// <param name="videoParser">The video parser.</param>
    /// <param name="localMediaScanner">The local media scanner.</param>
    /// <param name="fileNameBuilderService">The file name builder service.</param>
    /// <param name="strmValidationService">The STRM validation service.</param>
    /// <param name="ffmpegService">The ffmpeg Service.</param>
    /// <param name="downloadHistoryRepository">The Download History Repo.</param>
    /// <param name="configurationProvider">The Configuration Provider.</param>
    /// <param name="downloadQueueManager">The download queue manager.</param>
    /// <param name="originalVersionLanguageResolver">Resolves the correct original-version language code from the relevant broadcaster's own API.</param>
    public SubscriptionProcessor(
        ILogger<SubscriptionProcessor> logger,
        IMediathekViewApiClient apiClient,
        IVideoParser videoParser,
        ILocalMediaScanner localMediaScanner,
        IFileNameBuilderService fileNameBuilderService,
        IStrmValidationService strmValidationService,
        IFFmpegService ffmpegService,
        IDownloadHistoryRepository downloadHistoryRepository,
        IConfigurationProvider configurationProvider,
        IDownloadQueueManager downloadQueueManager,
        IOriginalVersionLanguageResolver originalVersionLanguageResolver)
    {
        _logger = logger;
        _apiClient = apiClient;
        _videoParser = videoParser;
        _localMediaScanner = localMediaScanner;
        _fileNameBuilderService = fileNameBuilderService;
        _strmValidationService = strmValidationService;
        _ffmpegService = ffmpegService;
        _downloadHistoryRepository = downloadHistoryRepository;
        _configurationProvider = configurationProvider;
        _downloadQueueManager = downloadQueueManager;
        _originalVersionLanguageResolver = originalVersionLanguageResolver;
    }

    /// <inheritdoc/>
    public async Task<int> ProcessSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        // Virtual subscriptions are not downloaded; their items are served through the channel.
        if (subscription.IsVirtual)
        {
            _logger.LogDebug("Skipping download for virtual subscription '{SubscriptionName}'.", subscription.Name);
            return 0;
        }

        var config = _configurationProvider.ConfigurationOrNull;
        if (config == null)
        {
            return 0;
        }

        var jobs = await GetJobsForSubscriptionAsync(
            subscription,
            config.Download.DownloadSubtitles,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Found {Count} new items for '{SubscriptionName}'.", jobs.Count, subscription.Name);

        foreach (var job in jobs)
        {
            _downloadQueueManager.QueueJob(job, subscription.Id);
        }

        if (jobs.Count > 0)
        {
            subscription.LastDownloadedTimestamp = DateTime.UtcNow;
        }

        return jobs.Count;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<(ResultItemDto Item, VideoInfo VideoInfo)> GetEligibleItemsAsync(
        Subscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in GetEligibleItemsAsync(subscription, honorHistory: true, cancellationToken).ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    /// <summary>
    /// Returns all items matching the subscription that should be surfaced in the virtual channel.
    /// Unlike <see cref="GetEligibleItemsAsync(Subscription, CancellationToken)"/> this does not filter by download history, so the
    /// channel always reflects the currently available items in the Mediathek.
    /// </summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching items.</returns>
    public async IAsyncEnumerable<(ResultItemDto Item, VideoInfo VideoInfo)> GetChannelItemsAsync(
        Subscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in GetEligibleItemsAsync(subscription, honorHistory: false, cancellationToken).ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    /// <summary>
    /// Resolves the best streamable video URL for a single API item, honoring the subscription's
    /// quality and fallback settings. Used by the virtual channel to build playable media sources.
    /// </summary>
    /// <param name="subscription">The subscription the item belongs to.</param>
    /// <param name="item">The API result item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The streamable URL, or <c>null</c> if none could be resolved.</returns>
    public async Task<string?> GetStreamUrlAsync(Subscription subscription, ResultItemDto item, CancellationToken cancellationToken = default)
    {
        var (url, _) = await GetUrlCandidate(item, subscription, cancellationToken).ConfigureAwait(false);
        return url;
    }

    /// <summary>
    /// Returns all items matching the subscription, optionally filtering out items that were
    /// already processed according to the download history.
    /// </summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="honorHistory">Whether to skip items already present in the download history.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching items.</returns>
    private async IAsyncEnumerable<(ResultItemDto Item, VideoInfo VideoInfo)> GetEligibleItemsAsync(
        Subscription subscription,
        bool honorHistory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LocalEpisodeCache? localEpisodeCache = null;
        if (subscription.Download.EnhancedDuplicateDetection && !subscription.IgnoreLocalFiles)
        {
            var subscriptionBaseDir = _fileNameBuilderService.GetSubscriptionBaseDirectory(subscription, DownloadContext.Subscription);
            if (!string.IsNullOrWhiteSpace(subscriptionBaseDir))
            {
                localEpisodeCache = _localMediaScanner.ScanDirectory(subscriptionBaseDir, subscription.Name);
            }
        }

        await foreach (var item in QueryApiAsync(subscription, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (honorHistory && !subscription.IgnoreHistory && await IsInDownloadCache(item, subscription.Id).ConfigureAwait(false))
            {
                _logger.LogDebug("Skipping item '{Title}' (ID: {Id}) as it was already processed for subscription '{SubscriptionName}'.", item.Title, item.Id, subscription.Name);
                continue;
            }

            var tempVideoInfo = _videoParser.ParseVideoInfo(subscription.Name, item.Title, item.Channel);
            if (tempVideoInfo != null && subscription.Metadata.KeepOriginalTitle)
            {
                tempVideoInfo.Title = item.Title;
            }

            await SetOvLanguageIfSetAsync(subscription, tempVideoInfo, item, cancellationToken).ConfigureAwait(false);

            if (tempVideoInfo != null && (subscription.Metadata.AppendDateToTitle || subscription.Metadata.AppendTimeToTitle))
            {
                var suffixParts = new List<string>();

                if (subscription.Metadata.AppendDateToTitle)
                {
                    var dateStr = item.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (!tempVideoInfo.Title.Contains(dateStr, StringComparison.OrdinalIgnoreCase))
                    {
                        suffixParts.Add(dateStr);
                    }
                }

                if (subscription.Metadata.AppendTimeToTitle)
                {
                    // using HH-mm because : is invalid in filenames
                    var timeStr = item.Timestamp.ToString("HH-mm", CultureInfo.InvariantCulture);
                    if (!tempVideoInfo.Title.Contains(timeStr, StringComparison.OrdinalIgnoreCase))
                    {
                        suffixParts.Add(timeStr);
                    }
                }

                if (suffixParts.Count > 0)
                {
                    tempVideoInfo.Title = $"{tempVideoInfo.Title} - {string.Join(" ", suffixParts)}";
                }

                tempVideoInfo.IsShow = true;
            }

            if (!await MatchesSubCriteriaAsync(tempVideoInfo, subscription, item, localEpisodeCache).ConfigureAwait(false))
            {
                continue;
            }

            yield return (item, tempVideoInfo!);
        }
    }

    /// <inheritdoc/>
    public async Task<List<DownloadJob>> GetJobsForSubscriptionAsync(
        Subscription subscription,
        bool downloadSubtitles,
        CancellationToken cancellationToken)
    {
        var jobs = new List<DownloadJob>();

        if (subscription.Download.DetectCrossResultAudioVariants)
        {
            // Buffer the whole eligible-item stream so sibling rows representing the same episode in a
            // different audio track (arte's channel/marker split, ZDF/ZDFneo/3sat's per-language rows)
            // can be grouped into one job instead of colliding as separate downloads to the same path.
            var eligibleItems = new List<(ResultItemDto Item, VideoInfo VideoInfo)>();
            await foreach (var eligible in GetEligibleItemsAsync(subscription, cancellationToken).ConfigureAwait(false))
            {
                eligibleItems.Add(eligible);
            }

            foreach (var group in AudioVariantGroupingService.GroupByEpisode(eligibleItems))
            {
                var job = await BuildDownloadJobAsync(subscription, downloadSubtitles, group.MainItem, group.MainVideoInfo, group.Secondaries, cancellationToken).ConfigureAwait(false);
                if (job != null)
                {
                    jobs.Add(job);
                }
            }

            return jobs;
        }

        await foreach (var (item, tempVideoInfo) in GetEligibleItemsAsync(subscription, cancellationToken).ConfigureAwait(false))
        {
            var job = await BuildDownloadJobAsync(subscription, downloadSubtitles, item, tempVideoInfo, Array.Empty<AudioVariantSecondary>(), cancellationToken).ConfigureAwait(false);
            if (job != null)
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    /// <summary>
    /// Builds the download job for a single main item, optionally with grouped-in secondary-audio
    /// siblings from <see cref="AudioVariantGroupingService"/>.
    /// </summary>
    /// <returns>The built job, or null if paths/URL resolution failed and the item should be skipped.</returns>
    private async Task<DownloadJob?> BuildDownloadJobAsync(
        Subscription subscription,
        bool downloadSubtitles,
        ResultItemDto item,
        VideoInfo tempVideoInfo,
        IReadOnlyList<AudioVariantSecondary> crossResultSecondaries,
        CancellationToken cancellationToken)
    {
        var paths = _fileNameBuilderService.GenerateDownloadPaths(tempVideoInfo, subscription, DownloadContext.Subscription);
        if (!paths.IsValid)
        {
            return null;
        }

        var (videoUrl, videoUrlFallbacks) = await GetUrlCandidate(item, subscription, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            return null;
        }

        // Subtitle URL for media metadata (only the preferred one).
        string? preferredSubtitleUrl = downloadSubtitles ? item.GetSubtitle()?.Url : null;

        // Download Task
        var downloadJob = new DownloadJob
        {
            ItemId = item.Id,
            Title = tempVideoInfo.Title,
            ItemInfo = tempVideoInfo,
            MediaMetadata = MediaMetadataFactory.Create(item, videoUrl, preferredSubtitleUrl, tempVideoInfo),
        };

        // Video/Main Item
        switch (paths.MainType)
        {
            case FileType.Strm:
                downloadJob.DownloadItems.Add(new DownloadItem { SourceUrl = videoUrl, FallbackSourceUrls = videoUrlFallbacks, DestinationPath = paths.MainFilePath, JobType = DownloadType.StreamingUrl });
                break;
            case FileType.Video:
                downloadJob.DownloadItems.Add(new DownloadItem { SourceUrl = videoUrl, FallbackSourceUrls = videoUrlFallbacks, DestinationPath = paths.MainFilePath, JobType = DownloadType.FFmpegDownload, CleanAudioTrackLabel = subscription.Download.CleanAudioTrackLabels });

                if (subscription.Download.DetectUndetectedSecondaryAudio)
                {
                    foreach (var candidate in SecondaryAudioUrlHelper.DetectCandidates(videoUrl))
                    {
                        if (!SecondaryAudioUrlHelper.IsKindEnabled(subscription.Download, candidate.Kind))
                        {
                            continue;
                        }

                        var candidateLang = await ResolveSecondaryAudioLanguageAsync(subscription, item, candidate, cancellationToken).ConfigureAwait(false);

                        // Standalone file next to the main video, e.g. "Title.eng.mka" or "Title [AD].deu.mka" -
                        // same naming convention already used for secondary-language items found via the API,
                        // and self-contained (no dependency on any other job finishing first).
                        var kindTag = candidate.Kind == SecondaryAudioKind.AudioDescription ? " [AD]" : string.Empty;
                        var standaloneDestination = Path.ChangeExtension(paths.MainFilePath, null) + kindTag + "." + candidateLang + ".mka";

                        downloadJob.DownloadItems.Add(new DownloadItem
                        {
                            SourceUrl = candidate.Url,
                            DestinationPath = standaloneDestination,
                            Language = candidateLang,
                            IsAudioDescription = candidate.Kind == SecondaryAudioKind.AudioDescription,
                            CleanAudioTrackLabel = subscription.Download.CleanAudioTrackLabels,
                            JobType = DownloadType.AudioExtraction
                        });
                    }
                }

                if (crossResultSecondaries.Count > 0)
                {
                    await AddCrossResultSecondariesAsync(downloadJob, subscription, paths.MainFilePath, crossResultSecondaries, cancellationToken).ConfigureAwait(false);
                }

                break;
            case FileType.Audio:
                // Prefer the broadcaster's own metadata for the real original-version language
                // (ARD-family stations via the page-gateway API, arte via its player-config API),
                // falling back to the title-parsed language (set via job.ItemInfo) for broadcasters
                // with no known resolver (ZDF, 3sat - which already tag the real language in the
                // title text directly, so there's nothing to resolve) or for audio-description
                // items, where a language lookup doesn't apply.
                var resolvedLang = (tempVideoInfo.HasAudiodescription || !subscription.Download.ResolveOriginalVersionLanguage)
                    ? null
                    : await _originalVersionLanguageResolver
                        .TryGetOriginalVersionLanguageAsync(item.UrlWebsite, cancellationToken)
                        .ConfigureAwait(false);

                downloadJob.DownloadItems.Add(new DownloadItem
                {
                    SourceUrl = videoUrl,
                    FallbackSourceUrls = videoUrlFallbacks,
                    DestinationPath = paths.MainFilePath,
                    Language = resolvedLang,
                    CleanAudioTrackLabel = subscription.Download.CleanAudioTrackLabels,
                    JobType = DownloadType.AudioExtraction
                });

                if (crossResultSecondaries.Count > 0)
                {
                    await AddCrossResultSecondariesAsync(downloadJob, subscription, paths.MainFilePath, crossResultSecondaries, cancellationToken).ConfigureAwait(false);
                }

                break;
            // Subtitles are downloaded separately.
            case FileType.Subtitle:
            default:
                _logger.LogError("Unknown file type '{FileType}'.", paths.MainType);
                break;
        }

        if (!HasRequiredAudioLanguage(subscription, tempVideoInfo, downloadJob))
        {
            _logger.LogDebug(
                "Skipping item '{Title}' - no audio track in the required language '{RequiredLanguage}' was found (main track: '{MainLanguage}').",
                item.Title,
                subscription.Accessibility.RequiredAudioLanguage,
                tempVideoInfo.Language);
            return null;
        }

        // Subtitle Item
        if (downloadSubtitles)
        {
            foreach (var sub in item.SubtitleUrls)
            {
                if (sub.Type == SubtitleType.Unknown)
                {
                    continue;
                }

                string subPath = paths.SubtitleFilePath;
                if (sub.Type == SubtitleType.WEBVTT)
                {
                    subPath = Path.ChangeExtension(subPath, ".vtt");
                }

                downloadJob.DownloadItems.Add(new DownloadItem { SourceUrl = sub.Url, DestinationPath = subPath, JobType = DownloadType.SubtitleDownload });
            }
        }

        if (subscription.Metadata.CreateNfo)
        {
            var topic = string.IsNullOrWhiteSpace(subscription.Name) ? item.Topic : subscription.Name;

            downloadJob.NfoMetadata = new NfoDTO()
            {
                Title = tempVideoInfo.Title,
                Description = item.Description,
                Show = tempVideoInfo.SeasonNumber.HasValue ? topic : string.Empty,
                Season = tempVideoInfo.SeasonNumber,
                Episode = tempVideoInfo.EpisodeNumber,
                Id = item.Id,
                FilePath = paths.NfoFilePath,
                Studio = item.Channel,
                RunTime = item.Duration,
                AirDate = item.Timestamp.DateTime,
                Set = string.Empty
            };
        }

        return downloadJob;
    }

    /// <summary>
    /// Resolves the language a single URL-derived secondary-audio candidate (ARD-style, see
    /// <see cref="SecondaryAudioUrlHelper"/>) ends up tagged with - the broadcaster-API lookup for
    /// original-version tracks when <see cref="BaseDownloadSettings.ResolveOriginalVersionLanguage"/>
    /// is enabled, falling back to the URL-derived placeholder otherwise. Shared by
    /// <see cref="BuildDownloadJobAsync"/> (which turns the result into a real
    /// <see cref="DownloadItem"/>) and the dry-run language-filter probe in
    /// <see cref="WouldPassAudioLanguageFilterAsync"/> (which only needs the language, not a full
    /// download item) - so both agree on what a given candidate resolves to.
    /// </summary>
    private async Task<string?> ResolveSecondaryAudioLanguageAsync(Subscription subscription, ResultItemDto item, SecondaryAudioCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Kind != SecondaryAudioKind.OriginalVersion || !subscription.Download.ResolveOriginalVersionLanguage)
        {
            return candidate.LanguageCode;
        }

        _logger.LogInformation("Resolving original-version language for '{Title}' using UrlWebsite '{UrlWebsite}'.", item.Title, item.UrlWebsite ?? "(null)");
        return (await _originalVersionLanguageResolver.TryGetOriginalVersionLanguageAsync(item.UrlWebsite, cancellationToken).ConfigureAwait(false)) ?? candidate.LanguageCode;
    }

    /// <summary>
    /// Checks whether <paramref name="job"/> ends up with at least one audio track in the
    /// subscription's configured <see cref="AccessibilitySettings.RequiredAudioLanguage"/> - the main
    /// item's own language plus every secondary track already added to
    /// <see cref="DownloadJob.DownloadItems"/> by the time this is called (both the URL-derived
    /// tracks from <see cref="SecondaryAudioUrlHelper"/> and the cross-result tracks from
    /// <see cref="AudioVariantGroupingService"/> - whichever detection settings are enabled). Always
    /// true (no filtering) when the setting is unset, so this is a no-op for every subscription that
    /// hasn't opted in.
    /// </summary>
    private static bool HasRequiredAudioLanguage(Subscription subscription, VideoInfo mainVideoInfo, DownloadJob job)
    {
        var requiredLanguage = subscription.Accessibility.RequiredAudioLanguage;
        if (string.IsNullOrWhiteSpace(requiredLanguage))
        {
            return true;
        }

        if (string.Equals(mainVideoInfo.Language, requiredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return job.DownloadItems.Any(i => string.Equals(i.Language, requiredLanguage, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Dry-run counterpart to <see cref="HasRequiredAudioLanguage"/>, used by
    /// <see cref="TestSubscriptionAsync"/>: determines whether this item would end up with an audio
    /// track in the subscription's configured <see cref="AccessibilitySettings.RequiredAudioLanguage"/>,
    /// without building a full download job (no path/URL resolution, no <see cref="DownloadItem"/>s) -
    /// so the "Abo prüfen" preview reflects the same filter the real download applies, cheaply. Checks
    /// the same three sources <see cref="HasRequiredAudioLanguage"/> does once the job is built: the
    /// main item's own language, cross-result siblings already grouped in by
    /// <see cref="AudioVariantGroupingService"/>, and URL-derived candidates from
    /// <see cref="SecondaryAudioUrlHelper"/> (the only one needing a network lookup, and only when
    /// <see cref="BaseDownloadSettings.DetectUndetectedSecondaryAudio"/> and
    /// <see cref="BaseDownloadSettings.ResolveOriginalVersionLanguage"/> are both enabled).
    /// </summary>
    private async Task<bool> WouldPassAudioLanguageFilterAsync(
        Subscription subscription,
        ResultItemDto item,
        VideoInfo mainVideoInfo,
        IReadOnlyList<AudioVariantSecondary> crossResultSecondaries,
        CancellationToken cancellationToken)
    {
        var requiredLanguage = subscription.Accessibility.RequiredAudioLanguage;
        if (string.IsNullOrWhiteSpace(requiredLanguage))
        {
            return true;
        }

        if (string.Equals(mainVideoInfo.Language, requiredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var secondary in crossResultSecondaries)
        {
            if (!SecondaryAudioUrlHelper.IsKindEnabled(subscription.Download, secondary.Kind))
            {
                continue;
            }

            var lang = string.IsNullOrWhiteSpace(secondary.VideoInfo.Language) ? "und" : secondary.VideoInfo.Language;
            if (string.Equals(lang, requiredLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (subscription.Download.DetectUndetectedSecondaryAudio)
        {
            var videoUrl = item.VideoUrls.OrderByDescending(v => v.Quality).Select(v => v.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
            foreach (var candidate in SecondaryAudioUrlHelper.DetectCandidates(videoUrl))
            {
                if (!SecondaryAudioUrlHelper.IsKindEnabled(subscription.Download, candidate.Kind))
                {
                    continue;
                }

                var candidateLang = await ResolveSecondaryAudioLanguageAsync(subscription, item, candidate, cancellationToken).ConfigureAwait(false);
                if (string.Equals(candidateLang, requiredLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Adds sibling rows grouped in by <see cref="AudioVariantGroupingService"/> as standalone
    /// secondary-audio files, resolving each sibling's own video URL individually since it's a
    /// distinct search result with its own quality URLs, not a derived variant of the main URL.
    /// </summary>
    private async Task AddCrossResultSecondariesAsync(
        DownloadJob downloadJob,
        Subscription subscription,
        string mainFilePath,
        IReadOnlyList<AudioVariantSecondary> secondaries,
        CancellationToken cancellationToken)
    {
        foreach (var secondary in secondaries)
        {
            if (!SecondaryAudioUrlHelper.IsKindEnabled(subscription.Download, secondary.Kind))
            {
                continue;
            }

            var (secondaryUrl, secondaryUrlFallbacks) = await GetUrlCandidate(secondary.Item, subscription, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(secondaryUrl))
            {
                _logger.LogWarning("Could not resolve a video URL for grouped audio-variant sibling '{Title}' (ID: {Id}); skipping this track.", secondary.Item.Title, secondary.Item.Id);
                continue;
            }

            var lang = string.IsNullOrWhiteSpace(secondary.VideoInfo.Language) ? "und" : secondary.VideoInfo.Language;
            var kindTag = secondary.Kind == SecondaryAudioKind.AudioDescription ? " [AD]" : string.Empty;
            var standaloneDestination = Path.ChangeExtension(mainFilePath, null) + kindTag + "." + lang + ".mka";

            downloadJob.DownloadItems.Add(new DownloadItem
            {
                SourceUrl = secondaryUrl,
                FallbackSourceUrls = secondaryUrlFallbacks,
                DestinationPath = standaloneDestination,
                Language = lang,
                IsAudioDescription = secondary.Kind == SecondaryAudioKind.AudioDescription,
                CleanAudioTrackLabel = subscription.Download.CleanAudioTrackLabels,
                JobType = DownloadType.AudioExtraction,
                SourceItemId = secondary.Item.Id
            });
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ResultItemDto> TestSubscriptionAsync(
        Subscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // For dry-run/test, we do not scan the disk for duplicate detection to avoid security risks (CA3003)
        // and because we want to test the query logic primarily.
        // We ensure IgnoreLocalFiles is set for this call.
        var testSub = subscription with { IgnoreLocalFiles = true };

        if (testSub.Download.DetectCrossResultAudioVariants)
        {
            // Mirror GetJobsForSubscriptionAsync's grouping: buffer the whole eligible-item stream so
            // sibling rows representing the same episode in a different audio track are grouped the
            // same way the real download would group them, before the audio-language filter below
            // gets a chance to look at them - otherwise a cross-result-only track (arte/ZDF/3sat) would
            // never be seen as available and every such item would incorrectly appear filtered out.
            var eligibleItems = new List<(ResultItemDto Item, VideoInfo VideoInfo)>();
            await foreach (var eligible in GetEligibleItemsAsync(testSub, cancellationToken).ConfigureAwait(false))
            {
                eligibleItems.Add(eligible);
            }

            foreach (var group in AudioVariantGroupingService.GroupByEpisode(eligibleItems))
            {
                var preview = await BuildTestPreviewAsync(testSub, group.MainItem, group.MainVideoInfo, group.Secondaries, cancellationToken).ConfigureAwait(false);
                if (preview != null)
                {
                    yield return preview;
                }
            }

            yield break;
        }

        await foreach (var (item, tempVideoInfo) in GetEligibleItemsAsync(testSub, cancellationToken).ConfigureAwait(false))
        {
            var preview = await BuildTestPreviewAsync(testSub, item, tempVideoInfo, Array.Empty<AudioVariantSecondary>(), cancellationToken).ConfigureAwait(false);
            if (preview != null)
            {
                yield return preview;
            }
        }
    }

    /// <summary>
    /// Builds one dry-run preview row for <see cref="TestSubscriptionAsync"/>, or null if the item
    /// wouldn't be downloaded at all because of <see cref="AccessibilitySettings.RequiredAudioLanguage"/>
    /// - matching how a real subscription run silently skips such items (via
    /// <see cref="HasRequiredAudioLanguage"/> inside <see cref="BuildDownloadJobAsync"/>), rather than
    /// showing a path for an item that would never actually be downloaded.
    /// </summary>
    private async Task<ResultItemDto?> BuildTestPreviewAsync(
        Subscription testSub,
        ResultItemDto item,
        VideoInfo tempVideoInfo,
        IReadOnlyList<AudioVariantSecondary> crossResultSecondaries,
        CancellationToken cancellationToken)
    {
        if (!await WouldPassAudioLanguageFilterAsync(testSub, item, tempVideoInfo, crossResultSecondaries, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var paths = _fileNameBuilderService.GenerateDownloadPaths(tempVideoInfo, testSub, DownloadContext.Subscription);
        string path = paths.MainFilePath;
        if (!paths.IsValid)
        {
            path = "Warnung: Ungültiger Pfad";
        }

        var description = item.Description ?? string.Empty;
        if (description.Length > 100)
        {
            description = string.Concat(description.AsSpan(0, 100), "...");
        }

        return item with { Description = $"Pfad: {path} | {description}" };
    }

    /// <summary>
    /// Applies filtering rules to determine if the item should be processed.
    /// </summary>
    /// <returns>True if the item passes all filters; otherwise, false.</returns>
    private async Task<bool> MatchesSubCriteriaAsync([NotNullWhen(true)] VideoInfo? tempVideoInfo, Subscription subscription, ResultItemDto item, LocalEpisodeCache? localEpisodeCache)
    {
        if (tempVideoInfo == null)
        {
            _logger.LogDebug("Skipping item '{Title}' due to video info parsing failure.", item.Title);
            return false;
        }

        if (localEpisodeCache != null && localEpisodeCache.Contains(tempVideoInfo))
        {
            _logger.LogInformation(
                "Skipping item '{Title}' (S{Season}E{Episode} / Abs: {Abs}) as it was found locally via enhanced duplicate detection.",
                item.Title,
                tempVideoInfo.SeasonNumber,
                tempVideoInfo.EpisodeNumber,
                tempVideoInfo.AbsoluteEpisodeNumber);

            // Backfill history only once per item/subscription - this runs on every subscription pass
            // that still sees the item in the search results (which can be many, e.g. one per manual
            // "Process" click or scheduled run), and without this guard it would insert a fresh row
            // with the current timestamp every single time, even though nothing was actually
            // downloaded. That made an already-existing file - possibly downloaded before the current
            // subscription settings were even set, e.g. an Audiodeskription track from before
            // "AllowAudioDescription" was turned off - jump back to the top of "Download Verlauf" with
            // a "just now" timestamp on every run, looking like a fresh download that never happened.
            if (!await _downloadHistoryRepository.ExistsByItemIdAndSubscriptionIdAsync(item.Id, subscription.Id).ConfigureAwait(false))
            {
                var localPath = localEpisodeCache.GetExistingFilePath(tempVideoInfo);
                await _downloadHistoryRepository.AddAsync(string.Empty, item.Id, subscription.Id, localPath!, item.Title, tempVideoInfo.Language).ConfigureAwait(false);
            }

            return false;
        }

        if (!subscription.Accessibility.AllowAudioDescription && tempVideoInfo.HasAudiodescription)
        {
            _logger.LogDebug("Skipping item '{Title}' due to Audiodescription and subscription preference.", item.Title);
            return false;
        }

        if (!subscription.Accessibility.AllowSignLanguage && tempVideoInfo.HasSignLanguage)
        {
            _logger.LogDebug("Skipping item '{Title}' due to Sign Language and subscription preference.", item.Title);
            return false;
        }

        if (subscription.Series.ExcludeSeries && tempVideoInfo.IsShow)
        {
            _logger.LogDebug("Skipping item '{Title}' because it was recognized as a series episode and ExcludeSeries is enabled.", item.Title);
            return false;
        }

        if (subscription.Series.EnforceSeriesParsing && !tempVideoInfo.IsShow && !subscription.Series.TreatNonEpisodesAsExtras)
        {
            _logger.LogDebug("Skipping item '{Title}' due to EnforceSeriesParsing and parsing result.", item.Title);
            return false;
        }

        if ((subscription.Series.EnforceSeriesParsing && !subscription.Series.AllowAbsoluteEpisodeNumbering && !tempVideoInfo.HasSeasonEpisodeNumbering) && (!subscription.Series.TreatNonEpisodesAsExtras && !tempVideoInfo.IsShow))
        {
            _logger.LogDebug("Skipping item '{Title}' due to absolute episode numbering and subscription preference.", item.Title);
            return false;
        }

        if (subscription.Series.TreatNonEpisodesAsExtras)
        {
            if (tempVideoInfo.IsTrailer && !subscription.Series.SaveTrailers)
            {
                _logger.LogDebug("Skipping item '{Title}' because it is a trailer and SaveTrailers is disabled.", item.Title);
                return false;
            }

            if (tempVideoInfo.IsInterview && !subscription.Series.SaveInterviews)
            {
                _logger.LogDebug("Skipping item '{Title}' because it is an interview and SaveInterviews is disabled.", item.Title);
                return false;
            }

            if (tempVideoInfo is { IsTrailer: false, IsInterview: false, IsShow: false } && !subscription.Series.SaveGenericExtras)
            {
                _logger.LogDebug("Skipping item '{Title}' because it is a generic extra and SaveGenericExtras is disabled.", item.Title);
                return false;
            }
        }

        return true;
    }

    private async Task<bool> IsInDownloadCache(ResultItemDto item, Guid subscriptionId)
    {
        // Primary check by the (possibly unstable) API item ID.
        if (await _downloadHistoryRepository.ExistsByItemIdAndSubscriptionIdAsync(item.Id, subscriptionId).ConfigureAwait(false))
        {
            return true;
        }

        // Secondary, more robust check by video URL. The API item ID can change when an entry is
        // re-published or de-duplicated, but the actual video URL stays the same. This prevents
        // extras and one-off (non-series) items from being downloaded again and again.
        var urls = item.VideoUrls.Select(v => v.Url).Where(u => !string.IsNullOrWhiteSpace(u));
        return await _downloadHistoryRepository.ExistsByAnyUrlAndSubscriptionIdAsync(urls, subscriptionId).ConfigureAwait(false);
    }

    /// <summary>
    /// Fills in a real language for items the title parser only recognized as a generic
    /// original-version marker (e.g. ARD's "(OV)"/"(Originalversion)" or arte's
    /// "(Originalversion mit Untertitel)"), which by itself doesn't say which language. Tries the
    /// broadcaster resolver first (works for any item MediathekViewWeb already returned as a
    /// distinct search result, not just the ones <see cref="SecondaryAudioUrlHelper"/> derives from
    /// a main video URL); falls back to the subscription's manually configured OriginalLanguage
    /// override if the resolver found nothing or is disabled.
    /// </summary>
    private async Task SetOvLanguageIfSetAsync(Subscription subscription, VideoInfo? videoInfo, ResultItemDto item, CancellationToken cancellationToken)
    {
        if (videoInfo is not { Language: "und" })
        {
            return;
        }

        if (subscription.Download.ResolveOriginalVersionLanguage)
        {
            var resolvedLanguage = await _originalVersionLanguageResolver
                .TryGetOriginalVersionLanguageAsync(item.UrlWebsite, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(resolvedLanguage))
            {
                videoInfo.Language = resolvedLanguage;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(subscription.Metadata.OriginalLanguage))
        {
            videoInfo.Language = subscription.Metadata.OriginalLanguage;
        }
    }

    /// <summary>
    /// Gets the best available URL candidate for downloading the video.
    /// </summary>
    /// <param name="item">The item to get the url for.</param>
    /// <param name="subscription">The subscription.</param>
    /// <param name="cancellationToken">The cancellationToken.</param>
    /// <returns>The best URL candidate, or null if none found.</returns>
    private async Task<(string? Url, IReadOnlyList<string> Fallbacks)> GetUrlCandidate(ResultItemDto item, Subscription subscription, CancellationToken cancellationToken = default)
    {
        // Quality: 3=HD, 2=Std, 1=Low
        var hdUrl = item.VideoUrls.FirstOrDefault(v => v.Quality == 3)?.Url;

        // If no fallback is allowed, return HD URL if available - and no fallback candidates either,
        // since the subscription explicitly opted out of ever falling back to a lower quality, at
        // discovery time or (see DownloadItem.FallbackSourceUrls) at download-execution time.
        if (!subscription.Download.AllowFallbackToLowerQuality)
        {
            return (hdUrl, Array.Empty<string>());
        }

        List<string> candidateUrls = item.VideoUrls.OrderByDescending(s => s.Quality).Select(s => s.Url).ToList();

        // If no url availability check is required, return the first URL - still with the rest of
        // the list as execution-time fallbacks, since this candidate was never actually validated.
        if (!subscription.Download.QualityCheckWithUrl)
        {
            return candidateUrls.Count > 0
                ? (candidateUrls[0], candidateUrls.Skip(1).ToList())
                : (null, Array.Empty<string>());
        }

        string? candidateUrl = null;

        var validCandidates = candidateUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();

        foreach (var url in validCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await _strmValidationService.ValidateUrlAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    candidateUrl = url;
                    if (url != validCandidates.First())
                    {
                        _logger.LogWarning("Primary quality download failed for '{Title}'. Fallback to: {Url}", item.Title, url);
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to validate URL '{Url}' for '{Title}'. Trying next quality...", url, item.Title);
            }
        }

        if (string.IsNullOrWhiteSpace(candidateUrl))
        {
            _logger.LogWarning("No valid video URL found for item '{Title}'.", item.Title);
            return (null, Array.Empty<string>());
        }

        // The remaining candidates (in the same best-first order) are worth trying again at
        // download-execution time if candidateUrl itself has since gone stale - see
        // DownloadItem.FallbackSourceUrls.
        var fallbacks = validCandidates.Where(u => u != candidateUrl).ToList();
        return (candidateUrl, fallbacks);
    }

    /// <summary>
    /// Queries the MediathekView API for results matching the subscription.
    /// </summary>
    /// <param name="subscription">The subscription to query for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection of result items retrieved from the API.</returns>
    private async IAsyncEnumerable<ResultItemDto> QueryApiAsync(Subscription subscription, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var currentPage = 0;
        var hasMoreResults = true;
        var pageSize = _configurationProvider.Configuration.Search.PageSize;
        var maxPages = _configurationProvider.Configuration.Search.MaxPages;

        while (hasMoreResults && currentPage < maxPages)
        {
            var apiQuery = new ApiQueryDto
            {
                Queries = subscription.Search.Criteria,
                Size = pageSize,
                Offset = currentPage * pageSize,
                MinDuration = subscription.Search.MinDurationMinutes * 60,
                MaxDuration = subscription.Search.MaxDurationMinutes * 60,
                MinBroadcastDate = subscription.Search.MinBroadcastDate,
                MaxBroadcastDate = subscription.Search.MaxBroadcastDate,
                Future = _configurationProvider.Configuration.Search.SearchInFutureBroadcasts,
            };

            QueryResultDto result;
            try
            {
                result = await _apiClient.SearchAsync(apiQuery, cancellationToken).ConfigureAwait(false);
            }
            catch (MediathekException ex)
            {
                _logger.LogWarning(ex, "Could not retrieve search results for subscription '{SubscriptionName}' due to an API error.", subscription.Name);
                yield break;
            }

            if (result.QueryInfo.TotalResults > (currentPage + 1) * pageSize)
            {
                hasMoreResults = true;
                currentPage++;
            }
            else
            {
                hasMoreResults = false;
            }

            foreach (var item in result.Results)
            {
                yield return item;
            }
        }
    }
}

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
    private readonly IUndefinedAudioLanguageBackfill _undefinedAudioLanguageBackfill;
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
    /// <param name="undefinedAudioLanguageBackfill">Fills in the language of audio tracks that were stored as "und" before it was known.</param>
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
        IOriginalVersionLanguageResolver originalVersionLanguageResolver,
        IUndefinedAudioLanguageBackfill undefinedAudioLanguageBackfill)
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
        _undefinedAudioLanguageBackfill = undefinedAudioLanguageBackfill;
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
        await foreach (var entry in GetEligibleItemsAsync(subscription, honorHistory: true, BuildLocalEpisodeCache(subscription), cancellationToken).ConfigureAwait(false))
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
        await foreach (var entry in GetEligibleItemsAsync(subscription, honorHistory: false, BuildLocalEpisodeCache(subscription), cancellationToken).ConfigureAwait(false))
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
    /// Scans the subscription's target directory for episodes already on disk, or returns null when
    /// duplicate detection is off (or explicitly bypassed, as in the dry run).
    /// </summary>
    /// <param name="subscription">The subscription whose target directory to scan.</param>
    /// <returns>The scanned episodes, or null when duplicate detection does not apply.</returns>
    private LocalEpisodeCache? BuildLocalEpisodeCache(Subscription subscription)
    {
        if (!subscription.Download.EnhancedDuplicateDetection || subscription.IgnoreLocalFiles)
        {
            return null;
        }

        var subscriptionBaseDir = _fileNameBuilderService.GetSubscriptionBaseDirectory(subscription, DownloadContext.Subscription);
        return string.IsNullOrWhiteSpace(subscriptionBaseDir)
            ? null
            : _localMediaScanner.ScanDirectory(subscriptionBaseDir, subscription.Name);
    }

    /// <summary>
    /// Fills in the real language of audio tracks this subscription stored as "und" earlier, now
    /// that one is configured. Cheap when there is nothing to do: without a configured language it
    /// returns immediately, and otherwise it is a single directory walk.
    /// </summary>
    /// <param name="subscription">The subscription whose target directory to update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the directory has been walked.</returns>
    private async Task BackfillUndefinedAudioLanguagesAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var configuredLanguage = GetConfiguredOriginalLanguage(subscription);
        if (!subscription.Metadata.BackfillAudioLanguages
            || OriginalVersionLanguagePolicy.IsUndefined(configuredLanguage)
            || subscription.IsVirtual
            || subscription.Download.UseStreamingUrlFiles)
        {
            return;
        }

        var baseDirectory = _fileNameBuilderService.GetSubscriptionBaseDirectory(subscription, DownloadContext.Subscription);
        var updated = await _undefinedAudioLanguageBackfill
            .BackfillAsync(baseDirectory, configuredLanguage, recursive: true, cancellationToken)
            .ConfigureAwait(false);

        if (updated > 0)
        {
            // Files were renamed under the scanner's feet. Its result is remembered for minutes and
            // shared across subscriptions, so without this every pass in that window would keep
            // seeing the placeholder names and fetch the tracks again.
            _localMediaScanner.InvalidateCache();
        }
    }

    /// <summary>
    /// Returns all items matching the subscription, optionally filtering out items that were
    /// already processed according to the download history.
    /// </summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="honorHistory">Whether to skip items already present in the download history.</param>
    /// <param name="localEpisodeCache">Episodes already on disk, or null when duplicate detection does not apply.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching items.</returns>
    private async IAsyncEnumerable<(ResultItemDto Item, VideoInfo VideoInfo)> GetEligibleItemsAsync(
        Subscription subscription,
        bool honorHistory,
        LocalEpisodeCache? localEpisodeCache,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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

        // An original language configured after the first downloads leaves "*.und.mka" files behind;
        // fill those in before anything reads the library, so what follows sees the renamed files.
        // Reading first and renaming afterwards is what made the plugin re-download a track it had
        // just renamed: the scan below still said "und", so the "eng" track looked missing.
        await BackfillUndefinedAudioLanguagesAsync(subscription, cancellationToken).ConfigureAwait(false);

        // Built once and threaded through: the eligible-item filter uses it to drop episodes already
        // on disk, and BuildDownloadJobAsync uses it to attach a new audio variant to the video that
        // is already there instead of fetching a second copy of the same episode.
        var localEpisodeCache = BuildLocalEpisodeCache(subscription);

        if (SecondaryAudioUrlHelper.AnyCrossResultDetectionEnabled(subscription.Download, subscription.Accessibility))
        {
            // Buffer the whole eligible-item stream so sibling rows representing the same episode in a
            // different audio track (arte's channel/marker split, ZDF/ZDFneo/3sat's per-language rows)
            // can be grouped into one job instead of colliding as separate downloads to the same path.
            var eligibleItems = new List<(ResultItemDto Item, VideoInfo VideoInfo)>();
            await foreach (var eligible in GetEligibleItemsAsync(subscription, honorHistory: true, localEpisodeCache, cancellationToken).ConfigureAwait(false))
            {
                eligibleItems.Add(eligible);
            }

            foreach (var rawGroup in AudioVariantGroupingService.GroupByEpisode(eligibleItems))
            {
                var group = SelectMainByLanguage(subscription, rawGroup);
                if (group == null)
                {
                    _logger.LogInformation(
                        "Skipping '{Title}': no version in a selected language is available.",
                        rawGroup.MainItem.Title);
                    continue;
                }

                var job = await BuildDownloadJobAsync(subscription, downloadSubtitles, group.MainItem, group.MainVideoInfo, group.Secondaries, localEpisodeCache, cancellationToken).ConfigureAwait(false);
                if (job != null)
                {
                    ClaimDestinations(job, localEpisodeCache);
                    jobs.Add(job);
                }
            }

            return jobs;
        }

        await foreach (var (item, tempVideoInfo) in GetEligibleItemsAsync(subscription, honorHistory: true, localEpisodeCache, cancellationToken).ConfigureAwait(false))
        {
            var job = await BuildDownloadJobAsync(subscription, downloadSubtitles, item, tempVideoInfo, Array.Empty<AudioVariantSecondary>(), localEpisodeCache, cancellationToken).ConfigureAwait(false);
            if (job != null)
            {
                ClaimDestinations(job, localEpisodeCache);
                jobs.Add(job);
            }
        }

        return jobs;
    }

    /// <summary>
    /// Adds a download item to <paramref name="job"/> unless the job already writes that file.
    /// </summary>
    /// <param name="job">The job to add to.</param>
    /// <param name="downloadItem">The item to add.</param>
    /// <returns>True if the item was added.</returns>
    private bool TryAddDownloadItem(DownloadJob job, DownloadItem downloadItem)
    {
        var duplicate = job.DownloadItems.Any(existing =>
            string.Equals(existing.DestinationPath, downloadItem.DestinationPath, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            // Seen in a real log: every film with an English track got two identical
            // AudioExtraction items writing the same ".eng.mka". Two independent paths append
            // secondary tracks - the per-result candidate scan and the cross-result grouping - and
            // both derive the name from the language alone, so two rows resolving to the same
            // language collide. The second one is pure waste: it re-fetches a track the first one
            // already wrote and adds a second, identical history row.
            _logger.LogDebug(
                "Skipping a second download item for '{Path}' in job '{Title}' - that destination is already covered by this job.",
                downloadItem.DestinationPath,
                job.Title);
            return false;
        }

        job.DownloadItems.Add(downloadItem);
        return true;
    }

    /// <summary>
    /// Marks the files <paramref name="job"/> is going to write as taken, so nothing else in this
    /// run targets them again.
    /// </summary>
    /// <remarks>
    /// The scan is a picture of the library as it was before the run started, and downloads land
    /// minutes later, asynchronously. Without this, two subscriptions matching the same item both
    /// queue it - neither can see the other's history rows, which are kept per subscription - and
    /// only the download manager's File.Exists check, by then far too late, stops the second one
    /// from doing the work twice. Sonarr solves the same problem with a queue specification that
    /// rejects a release already being fetched; claiming the path is the same idea in the shape
    /// this plugin already has.
    /// </remarks>
    /// <param name="job">The job whose destinations to claim.</param>
    /// <param name="localEpisodeCache">The shared picture of the target directory, or null when duplicate detection does not apply.</param>
    private static void ClaimDestinations(DownloadJob job, LocalEpisodeCache? localEpisodeCache)
    {
        if (localEpisodeCache == null)
        {
            return;
        }

        foreach (var downloadItem in job.DownloadItems)
        {
            localEpisodeCache.ClaimFile(downloadItem.DestinationPath);
        }
    }

    /// <summary>
    /// Builds an audio-only job that attaches <paramref name="item"/>'s audio to an episode already
    /// on disk, when that episode exists locally in a different language. Returns null when the
    /// feature is off, no local video matches, or this language is already present - in which case
    /// the caller falls through to a normal download.
    /// </summary>
    /// <param name="subscription">The subscription the item belongs to.</param>
    /// <param name="item">The API result the audio would be taken from.</param>
    /// <param name="tempVideoInfo">The parsed episode information for <paramref name="item"/>.</param>
    /// <param name="localEpisodeCache">Episodes already on disk, or null when duplicate detection does not apply.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An audio-only job, or null when the caller should build a normal download job.</returns>
    /// <remarks>
    /// The track is written *next to* the existing video as a ".mka", exactly like the secondary
    /// tracks produced for freshly downloaded episodes. Jellyfin then presents the episode as one
    /// item with selectable audio, and the existing video file is never opened, rewritten or moved -
    /// which matters because it is a file already in the user's library.
    /// </remarks>
    private async Task<DownloadJob?> TryBuildAudioForExistingEpisodeAsync(
        Subscription subscription,
        ResultItemDto item,
        VideoInfo tempVideoInfo,
        LocalEpisodeCache? localEpisodeCache,
        CancellationToken cancellationToken)
    {
        // Every kind is attached on its own switch - a subscription can collect one without the
        // others.
        var kind = tempVideoInfo switch
        {
            { HasAudiodescription: true } => SecondaryAudioKind.AudioDescription,
            { HasClearLanguage: true } => SecondaryAudioKind.ClearSpeech,
            _ => SecondaryAudioKind.OriginalVersion,
        };

        var attachingEnabled = kind switch
        {
            SecondaryAudioKind.AudioDescription => subscription.Accessibility.AddAudioDescriptionToExistingEpisodes,
            SecondaryAudioKind.ClearSpeech => subscription.Accessibility.AddClearSpeechToExistingEpisodes,
            _ => subscription.Download.AddAudioToExistingEpisodes,
        };

        if (!attachingEnabled || localEpisodeCache == null)
        {
            return null;
        }

        // A virtual subscription streams rather than storing anything, so there is no file to attach to.
        if (subscription.IsVirtual || subscription.Download.UseStreamingUrlFiles)
        {
            return null;
        }

        if (!localEpisodeCache.TryGetEpisodeVideo(tempVideoInfo, out var existingVideoPath, out var existingLanguages))
        {
            return null;
        }

        var language = string.IsNullOrWhiteSpace(tempVideoInfo.Language) ? OriginalVersionLanguagePolicy.UndefinedLanguageCode : tempVideoInfo.Language;

        // An original-version row whose language nothing named: the subscription's setting decides
        // whether it is stored as "und", tagged with the configured language, or left out.
        if (kind == SecondaryAudioKind.OriginalVersion && OriginalVersionLanguagePolicy.IsUndefined(language))
        {
            var undefinedDecision = OriginalVersionLanguagePolicy.Decide(
                null,
                GetConfiguredOriginalLanguage(subscription),
                subscription.Metadata.UndefinedOriginalVersionHandling);

            if (undefinedDecision.IsSkipped)
            {
                _logger.LogInformation("Skipping the audio track of '{Title}': {Reason}", item.Title, undefinedDecision.SkipReason);
                return null;
            }

            language = undefinedDecision.LanguageCode!;
        }

        if (!IsAudioLanguageKept(subscription, kind, language))
        {
            _logger.LogInformation(
                "Skipping the '{Language}' audio track of '{Title}': the subscription only stores selected languages.",
                language,
                item.Title);
            return null;
        }

        // The same track may already be sitting there as "und" from an earlier run, back when nobody
        // named its language - now that one is known, renaming and re-tagging that file is both
        // cheaper and more correct than downloading the very same audio a second time.
        if (subscription.Metadata.BackfillAudioLanguages
            && existingLanguages.Contains(OriginalVersionLanguagePolicy.UndefinedLanguageCode, StringComparer.OrdinalIgnoreCase)
            && !existingLanguages.Contains(language, StringComparer.OrdinalIgnoreCase)
            && await _undefinedAudioLanguageBackfill.BackfillEpisodeAsync(existingVideoPath, language, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Filled in the language '{Language}' for the existing audio track of '{ExistingPath}' instead of downloading it again.",
                language,
                existingVideoPath);

            // Same reason as in BackfillUndefinedAudioLanguagesAsync: the remembered scan now names
            // a file that no longer exists under that name.
            _localMediaScanner.InvalidateCache();
            return null;
        }

        // Already there - either as the existing video's own audio or as a track added on an earlier
        // run. Without this the same track would be re-fetched on every single pass.
        if (existingLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var (audioUrl, audioUrlFallbacks) = await GetUrlCandidate(item, subscription, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            return null;
        }

        var kindTag = tempVideoInfo.HasAudiodescription ? " [AD]" : string.Empty;
        var destination = Path.ChangeExtension(existingVideoPath, null) + kindTag + "." + language + ".mka";

        _logger.LogInformation(
            "Attaching '{Language}' audio of '{Title}' to the existing episode '{ExistingPath}' instead of downloading a second video.",
            language,
            item.Title,
            existingVideoPath);

        var downloadJob = new DownloadJob
        {
            ItemId = item.Id,
            Title = tempVideoInfo.Title,
            ItemInfo = tempVideoInfo,
            MediaMetadata = MediaMetadataFactory.Create(item, audioUrl, null, tempVideoInfo),
        };

        downloadJob.DownloadItems.Add(new DownloadItem
        {
            SourceUrl = audioUrl,
            FallbackSourceUrls = audioUrlFallbacks,
            DestinationPath = destination,
            Language = language,
            IsAudioDescription = tempVideoInfo.HasAudiodescription,
            CleanAudioTrackLabel = subscription.Download.CleanAudioTrackLabels,
            JobType = DownloadType.AudioExtraction
        });

        return downloadJob;
    }

    /// <summary>
    /// Finds another version of the same item whose language the subscription does store, to be
    /// downloaded as the main video in place of one it doesn't. Only original-version tracks
    /// qualify: an audio description or a "klare Sprache" track speaks the same language as the
    /// track it would replace, so swapping in one of those would change nothing about the language.
    /// </summary>
    /// <param name="subscription">The subscription the item belongs to.</param>
    /// <param name="item">The API result.</param>
    /// <param name="videoUrl">The resolved main video URL to derive versions from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The URL and language of the version to download instead, or null when there is none.</returns>
    /// <remarks>
    /// Deliberately independent of <see cref="BaseDownloadSettings.DetectUndetectedSecondaryAudio"/>:
    /// that setting is about collecting *additional* tracks next to the main video, while this is
    /// about which single version gets downloaded at all. Someone who asks for English only should
    /// get the English version without having to enable extra-track detection first.
    /// </remarks>
    private async Task<(string Url, string LanguageCode)?> TryPromoteAllowedVersionAsync(
        Subscription subscription,
        ResultItemDto item,
        string videoUrl,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in SecondaryAudioUrlHelper.DetectCandidates(videoUrl))
        {
            if (candidate.Kind != SecondaryAudioKind.OriginalVersion)
            {
                continue;
            }

            var decision = await ResolveSecondaryAudioLanguageAsync(subscription, item, candidate, cancellationToken).ConfigureAwait(false);
            if (decision.IsSkipped)
            {
                continue;
            }

            if (IsAudioLanguageKept(subscription, candidate.Kind, decision.LanguageCode))
            {
                return (candidate.Url, decision.LanguageCode!);
            }
        }

        return null;
    }

    /// <summary>
    /// Adjusts a grouped episode to the subscription's language selection: when the main row's
    /// language is not stored but a grouped sibling's is, that sibling becomes the main video and
    /// the rejected row drops out. Returns null when nothing in the group qualifies.
    /// </summary>
    /// <param name="subscription">The subscription the group belongs to.</param>
    /// <param name="group">The grouped episode.</param>
    /// <returns>The group to download, or null to skip the episode entirely.</returns>
    private AudioVariantGroup? SelectMainByLanguage(Subscription subscription, AudioVariantGroup group)
    {
        if (IsAudioLanguageKept(subscription, SecondaryAudioKind.OriginalVersion, group.MainVideoInfo.Language))
        {
            return group;
        }

        foreach (var secondary in group.Secondaries)
        {
            if (secondary.Kind != SecondaryAudioKind.OriginalVersion
                || !IsAudioLanguageKept(subscription, secondary.Kind, ResolveGroupedSecondaryLanguage(subscription, secondary)))
            {
                continue;
            }

            var remaining = group.Secondaries.Where(other => !ReferenceEquals(other, secondary)).ToList();
            _logger.LogInformation(
                "Using the '{Language}' version of '{Title}' as the main video: the '{RejectedLanguage}' one is not among the selected languages.",
                secondary.VideoInfo.Language,
                secondary.Item.Title,
                group.MainVideoInfo.Language);

            return new AudioVariantGroup(secondary.Item, secondary.VideoInfo, remaining);
        }

        return null;
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
        LocalEpisodeCache? localEpisodeCache,
        CancellationToken cancellationToken)
    {
        var audioOnlyJob = await TryBuildAudioForExistingEpisodeAsync(subscription, item, tempVideoInfo, localEpisodeCache, cancellationToken).ConfigureAwait(false);
        if (audioOnlyJob != null)
        {
            return audioOnlyJob;
        }

        var (videoUrl, videoUrlFallbacks) = await GetUrlCandidate(item, subscription, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            return null;
        }

        // The language selection decides before anything is written: when the main track's own
        // language is not one the subscription stores, another version of the same item has to take
        // its place - there would otherwise be no video file for its audio track to sit next to.
        // Resolved before the paths are built, because the language is part of the file name.
        if (!IsAudioLanguageKept(subscription, SecondaryAudioKind.OriginalVersion, tempVideoInfo.Language))
        {
            var replacement = await TryPromoteAllowedVersionAsync(subscription, item, videoUrl, cancellationToken).ConfigureAwait(false);
            if (replacement is null)
            {
                _logger.LogInformation(
                    "Skipping '{Title}': its audio is '{Language}' and no version in a selected language is available.",
                    item.Title,
                    tempVideoInfo.Language);
                return null;
            }

            _logger.LogInformation(
                "Downloading the '{Language}' version of '{Title}' as the main video: its own '{MainLanguage}' audio is not among the selected languages.",
                replacement.Value.LanguageCode,
                item.Title,
                tempVideoInfo.Language);

            videoUrl = replacement.Value.Url;
            videoUrlFallbacks = Array.Empty<string>();
            tempVideoInfo.Language = replacement.Value.LanguageCode;
        }

        var paths = _fileNameBuilderService.GenerateDownloadPaths(tempVideoInfo, subscription, DownloadContext.Subscription);
        if (!paths.IsValid)
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

                foreach (var candidate in SecondaryAudioUrlHelper.DetectCandidates(videoUrl))
                {
                    if (!SecondaryAudioUrlHelper.IsKindEnabled(subscription.Download, subscription.Accessibility, candidate.Kind, SecondaryAudioDetectionSource.UrlDerived))
                    {
                        continue;
                    }

                    var decision = await ResolveSecondaryAudioLanguageAsync(subscription, item, candidate, cancellationToken).ConfigureAwait(false);
                    if (decision.IsSkipped)
                    {
                        _logger.LogInformation("Skipping a secondary audio track of '{Title}': {Reason}", item.Title, decision.SkipReason);
                        continue;
                    }

                    var candidateLang = decision.LanguageCode!;
                    if (!IsAudioLanguageKept(subscription, candidate.Kind, candidateLang))
                    {
                        _logger.LogDebug(
                            "Skipping the '{Language}' audio track of '{Title}': not among the selected languages.",
                            candidateLang,
                            item.Title);
                        continue;
                    }

                    // Standalone file next to the main video, e.g. "Title.eng.mka" or "Title [AD].deu.mka" -
                    // same naming convention already used for secondary-language items found via the API,
                    // and self-contained (no dependency on any other job finishing first).
                    var kindTag = candidate.Kind == SecondaryAudioKind.AudioDescription ? " [AD]" : string.Empty;
                    var standaloneDestination = Path.ChangeExtension(paths.MainFilePath, null) + kindTag + "." + candidateLang + ".mka";

                    _ = TryAddDownloadItem(downloadJob, new DownloadItem
                    {
                        SourceUrl = candidate.Url,
                        DestinationPath = standaloneDestination,
                        Language = candidateLang,
                        IsAudioDescription = candidate.Kind == SecondaryAudioKind.AudioDescription,
                        CleanAudioTrackLabel = subscription.Download.CleanAudioTrackLabels,
                        JobType = DownloadType.AudioExtraction
                    });
                }

                if (crossResultSecondaries.Count > 0)
                {
                    await AddCrossResultSecondariesAsync(downloadJob, subscription, paths.MainFilePath, crossResultSecondaries, cancellationToken).ConfigureAwait(false);
                }

                break;
            case FileType.Audio:
                // The item is a standalone audio version (an original-version row downloaded as its
                // own track). Its language went through SetOvLanguageIfSetAsync already; only a
                // still-undetermined one needs the subscription's setting applied here, and an audio
                // description is always the main track's language and never undetermined.
                var audioLanguage = tempVideoInfo.Language;
                if (!tempVideoInfo.HasAudiodescription && OriginalVersionLanguagePolicy.IsUndefined(audioLanguage))
                {
                    var audioDecision = OriginalVersionLanguagePolicy.Decide(
                        null,
                        GetConfiguredOriginalLanguage(subscription),
                        subscription.Metadata.UndefinedOriginalVersionHandling);

                    if (audioDecision.IsSkipped)
                    {
                        _logger.LogInformation("Skipping '{Title}': {Reason}", item.Title, audioDecision.SkipReason);
                        return null;
                    }

                    audioLanguage = audioDecision.LanguageCode!;
                }

                downloadJob.DownloadItems.Add(new DownloadItem
                {
                    SourceUrl = videoUrl,
                    FallbackSourceUrls = videoUrlFallbacks,
                    DestinationPath = paths.MainFilePath,
                    Language = audioLanguage,
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
    /// <see cref="SecondaryAudioUrlHelper"/>) ends up tagged with: the broadcaster's own API first,
    /// then the subscription's setting for tracks whose language nothing names. Shared by
    /// <see cref="BuildDownloadJobAsync"/> (which turns the result into a real
    /// <see cref="DownloadItem"/>) and the dry-run language-filter probe in
    /// <see cref="WouldPassAudioLanguageFilterAsync"/>, so both agree on what a candidate resolves to.
    /// </summary>
    /// <param name="subscription">The subscription the item belongs to.</param>
    /// <param name="item">The API result the candidate was derived from.</param>
    /// <param name="candidate">The detected candidate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decision: a language to tag with, or a skip.</returns>
    private async Task<OriginalVersionLanguageDecision> ResolveSecondaryAudioLanguageAsync(Subscription subscription, ResultItemDto item, SecondaryAudioCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Kind != SecondaryAudioKind.OriginalVersion)
        {
            // Audio description and clear speech are always the main track's own language - nothing
            // to resolve and nothing that could be undetermined.
            return OriginalVersionLanguageDecision.Tag(candidate.LanguageCode);
        }

        _logger.LogInformation("Resolving original-version language for '{Title}' using UrlWebsite '{UrlWebsite}'.", item.Title, item.UrlWebsite ?? "(null)");
        var resolvedLanguage = await _originalVersionLanguageResolver
            .TryGetOriginalVersionLanguageAsync(item.UrlWebsite, cancellationToken)
            .ConfigureAwait(false);

        return OriginalVersionLanguagePolicy.Decide(
            resolvedLanguage,
            GetConfiguredOriginalLanguage(subscription),
            subscription.Metadata.UndefinedOriginalVersionHandling);
    }

    /// <summary>
    /// Gets the fallback language the user configured for original-version tracks whose language
    /// nothing names: the subscription's own setting first, then the global default.
    /// </summary>
    /// <param name="subscription">The subscription the item belongs to.</param>
    /// <returns>The configured language code, or null when neither level sets one.</returns>
    private string? GetConfiguredOriginalLanguage(Subscription subscription)
    {
        if (!string.IsNullOrWhiteSpace(subscription.Metadata.OriginalLanguage))
        {
            return subscription.Metadata.OriginalLanguage;
        }

        return _configurationProvider.ConfigurationOrNull?.SubscriptionDefaults.MetadataSettings.OriginalLanguage;
    }

    /// <summary>
    /// Determines whether a track in the given language is stored, given the subscription's language
    /// selection.
    /// </summary>
    /// <param name="subscription">The subscription the track belongs to.</param>
    /// <param name="kind">What kind of track it is.</param>
    /// <param name="languageCode">The language it would be tagged with.</param>
    /// <returns>True when the track is stored.</returns>
    /// <remarks>
    /// Accessibility tracks never go through the language filter: an audio description speaks the
    /// same language as the main track and is asked for by kind, not by language. An undetermined
    /// track survives the filter as well - the user already decided what should happen to those
    /// (see <see cref="UndefinedOriginalVersionHandling"/>), and choosing "store as und" over "skip"
    /// is exactly the statement "keep it, name unknown".
    /// </remarks>
    private static bool IsAudioLanguageKept(Subscription subscription, SecondaryAudioKind kind, string? languageCode)
    {
        if (kind != SecondaryAudioKind.OriginalVersion || OriginalVersionLanguagePolicy.IsUndefined(languageCode))
        {
            return true;
        }

        return AudioLanguageSelection.From(subscription.Download).Allows(languageCode);
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
        var required = RequiredAudioLanguages.From(subscription.Accessibility);
        if (required.AcceptsAnything || required.IsSatisfiedBy(mainVideoInfo.Language))
        {
            return true;
        }

        return job.DownloadItems.Any(i => required.IsSatisfiedBy(i.Language));
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
    /// <see cref="BaseDownloadSettings.DetectUndetectedSecondaryAudio"/> is enabled).
    /// </summary>
    private async Task<bool> WouldPassAudioLanguageFilterAsync(
        Subscription subscription,
        ResultItemDto item,
        VideoInfo mainVideoInfo,
        IReadOnlyList<AudioVariantSecondary> crossResultSecondaries,
        CancellationToken cancellationToken)
    {
        var required = RequiredAudioLanguages.From(subscription.Accessibility);
        if (required.AcceptsAnything)
        {
            return true;
        }

        if (required.IsSatisfiedBy(mainVideoInfo.Language))
        {
            return true;
        }

        foreach (var secondary in crossResultSecondaries)
        {
            if (!SecondaryAudioUrlHelper.IsKindEnabled(subscription.Download, subscription.Accessibility, secondary.Kind, SecondaryAudioDetectionSource.CrossResult))
            {
                continue;
            }

            if (required.IsSatisfiedBy(secondary.VideoInfo.Language))
            {
                return true;
            }
        }

        var videoUrl = item.VideoUrls.OrderByDescending(v => v.Quality).Select(v => v.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        foreach (var candidate in SecondaryAudioUrlHelper.DetectCandidates(videoUrl))
        {
            if (!SecondaryAudioUrlHelper.IsKindEnabled(subscription.Download, subscription.Accessibility, candidate.Kind, SecondaryAudioDetectionSource.UrlDerived))
            {
                continue;
            }

            var decision = await ResolveSecondaryAudioLanguageAsync(subscription, item, candidate, cancellationToken).ConfigureAwait(false);
            if (!decision.IsSkipped && required.IsSatisfiedBy(decision.LanguageCode))
            {
                return true;
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
            if (!SecondaryAudioUrlHelper.IsKindEnabled(subscription.Download, subscription.Accessibility, secondary.Kind, SecondaryAudioDetectionSource.CrossResult))
            {
                continue;
            }

            var secondaryLanguage = ResolveGroupedSecondaryLanguage(subscription, secondary);
            if (secondaryLanguage is null)
            {
                _logger.LogInformation(
                    "Skipping the original-version track of '{Title}': {Reason}",
                    secondary.Item.Title,
                    OriginalVersionLanguagePolicy.SkippedMessage);
                continue;
            }

            if (!IsAudioLanguageKept(subscription, secondary.Kind, secondaryLanguage))
            {
                _logger.LogDebug(
                    "Skipping the '{Language}' audio track of '{Title}': not among the selected languages.",
                    secondaryLanguage,
                    secondary.Item.Title);
                continue;
            }

            var (secondaryUrl, secondaryUrlFallbacks) = await GetUrlCandidate(secondary.Item, subscription, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(secondaryUrl))
            {
                _logger.LogWarning("Could not resolve a video URL for grouped audio-variant sibling '{Title}' (ID: {Id}); skipping this track.", secondary.Item.Title, secondary.Item.Id);
                continue;
            }

            var kindTag = secondary.Kind == SecondaryAudioKind.AudioDescription ? " [AD]" : string.Empty;
            var standaloneDestination = Path.ChangeExtension(mainFilePath, null) + kindTag + "." + secondaryLanguage + ".mka";

            _ = TryAddDownloadItem(downloadJob, new DownloadItem
            {
                SourceUrl = secondaryUrl,
                FallbackSourceUrls = secondaryUrlFallbacks,
                DestinationPath = standaloneDestination,
                Language = secondaryLanguage,
                IsAudioDescription = secondary.Kind == SecondaryAudioKind.AudioDescription,
                CleanAudioTrackLabel = subscription.Download.CleanAudioTrackLabels,
                JobType = DownloadType.AudioExtraction,
                SourceItemId = secondary.Item.Id
            });
        }
    }

    /// <summary>
    /// Gets the language a grouped-in sibling track is tagged with, applying the subscription's
    /// setting for original-version tracks whose language nothing names.
    /// </summary>
    /// <param name="subscription">The subscription the item belongs to.</param>
    /// <param name="secondary">The grouped-in sibling.</param>
    /// <returns>The language code, or null when the track is not stored at all.</returns>
    private string? ResolveGroupedSecondaryLanguage(Subscription subscription, AudioVariantSecondary secondary)
    {
        var language = secondary.VideoInfo.Language;
        if (secondary.Kind != SecondaryAudioKind.OriginalVersion || !OriginalVersionLanguagePolicy.IsUndefined(language))
        {
            return string.IsNullOrWhiteSpace(language) ? OriginalVersionLanguagePolicy.UndefinedLanguageCode : language;
        }

        var decision = OriginalVersionLanguagePolicy.Decide(
            null,
            GetConfiguredOriginalLanguage(subscription),
            subscription.Metadata.UndefinedOriginalVersionHandling);

        return decision.LanguageCode;
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

        if (SecondaryAudioUrlHelper.AnyCrossResultDetectionEnabled(testSub.Download, testSub.Accessibility))
        {
            // Mirror GetJobsForSubscriptionAsync's grouping: buffer the whole eligible-item stream so
            // sibling rows representing the same episode in a different audio track are grouped the
            // same way the real download would group them, before the audio-language filter below
            // gets a chance to look at them - otherwise a cross-result-only track (arte/ZDF/3sat) would
            // never be seen as available and every such item would incorrectly appear filtered out.
            var eligibleItems = new List<(ResultItemDto Item, VideoInfo VideoInfo)>();
            await foreach (var eligible in GetEligibleItemsAsync(testSub, honorHistory: true, localEpisodeCache: null, cancellationToken).ConfigureAwait(false))
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

        await foreach (var (item, tempVideoInfo) in GetEligibleItemsAsync(testSub, honorHistory: true, localEpisodeCache: null, cancellationToken).ConfigureAwait(false))
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
    /// Looks for a file already on disk that is this item, first by episode numbering and then by
    /// the exact path this item would be downloaded to.
    /// </summary>
    /// <remarks>
    /// The numbering index only ever contains files whose name yields a season/episode or absolute
    /// number, which leaves films and other unnumbered items out of duplicate detection entirely -
    /// measured on a real library, 945 of 952 scanned files under the film root were invisible to
    /// it. For those the only thing that stopped a re-download was the <c>File.Exists</c> check the
    /// download manager does per item, which happens after the job has been built and queued and
    /// leaves no history entry, so the same item was re-evaluated on every single run.
    /// <para>
    /// The generated path is the same name the download would write, so an existing file there is
    /// this item - including one put there by a *different* subscription, whose history rows this
    /// one cannot see. A different language is not caught by this and must not be: the file name
    /// carries the language for anything but German, so an English variant resolves to its own
    /// ".eng.mka" and still reaches the audio-attaching path in
    /// <see cref="TryBuildAudioForExistingEpisodeAsync"/>.
    /// </para>
    /// </remarks>
    /// <param name="videoInfo">The parsed info for the item, with title and language final.</param>
    /// <param name="subscription">The subscription being processed.</param>
    /// <param name="localEpisodeCache">The scan of the subscription's target directory.</param>
    /// <returns>
    /// The matching path and whether it is a file that exists (as opposed to one this run has only
    /// decided to write), or null if nothing matches. The distinction matters to the caller: only
    /// a file that is really there may be recorded in the download history.
    /// </returns>
    private (string Path, bool OnDisk)? FindLocalDuplicate(VideoInfo videoInfo, Subscription subscription, LocalEpisodeCache localEpisodeCache)
    {
        var byNumbering = localEpisodeCache.GetExistingFilePath(videoInfo);
        if (byNumbering != null)
        {
            return (byNumbering, true);
        }

        var paths = _fileNameBuilderService.GenerateDownloadPaths(videoInfo, subscription, DownloadContext.Subscription);
        if (paths is not { IsValid: true })
        {
            return null;
        }

        if (localEpisodeCache.ContainsFile(paths.MainFilePath))
        {
            return (paths.MainFilePath, true);
        }

        return localEpisodeCache.IsClaimed(paths.MainFilePath) ? (paths.MainFilePath, false) : null;
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

        var localMatch = localEpisodeCache == null ? null : FindLocalDuplicate(tempVideoInfo, subscription, localEpisodeCache);
        if (localMatch is { OnDisk: false } claimed)
        {
            // Something earlier in this run is already going to write that file - most often the
            // same item matched by a second subscription pointing at the same folder. Nothing goes
            // into the download history here: the file does not exist yet, and a plan that fails
            // must not leave behind a record saying it succeeded.
            _logger.LogInformation(
                "Skipping item '{Title}' as another download in this run already targets '{LocalPath}'.",
                item.Title,
                claimed.Path);
            return false;
        }

        if (localMatch is { OnDisk: true } onDisk)
        {
            var localPath = onDisk.Path;
            _logger.LogInformation(
                "Skipping item '{Title}' (S{Season}E{Episode} / Abs: {Abs}) as it was found locally via enhanced duplicate detection: '{LocalPath}'.",
                item.Title,
                tempVideoInfo.SeasonNumber,
                tempVideoInfo.EpisodeNumber,
                tempVideoInfo.AbsoluteEpisodeNumber,
                localPath);

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
                await _downloadHistoryRepository.AddAsync(string.Empty, item.Id, subscription.Id, localPath, item.Title, tempVideoInfo.Language).ConfigureAwait(false);
            }

            return false;
        }

        if (!subscription.Accessibility.AllowAudioDescription && tempVideoInfo.HasAudiodescription)
        {
            _logger.LogDebug("Skipping item '{Title}' due to Audiodescription and subscription preference.", item.Title);
            return false;
        }

        if (!subscription.Accessibility.DownloadClearSpeech && tempVideoInfo.HasClearLanguage)
        {
            _logger.LogDebug("Skipping item '{Title}' due to clear speech and subscription preference.", item.Title);
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

        // Only-absolute-numbering rejection ("Absolute Episodennummerierung erlauben" unticked).
        // The previous form of this check also required !IsShow, which made it unreachable: the
        // EnforceSeriesParsing check directly above already returns for every item that is not a
        // show, so nothing could ever satisfy both - the setting silently did nothing.
        // Deliberately keyed on HasAbsoluteNumbering rather than just "no season/episode": the
        // parser also flags season-only and (as a last resort) date-titled items as shows, and
        // those are not what this checkbox is about, so they keep passing through.
        if (subscription.Series.EnforceSeriesParsing
            && !subscription.Series.AllowAbsoluteEpisodeNumbering
            && tempVideoInfo.HasAbsoluteNumbering
            && !tempVideoInfo.HasSeasonEpisodeNumbering)
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
    /// original-version marker (e.g. ARD's "(OV)"/"(Originalversion)" or arte's "(Originalversion
    /// mit Untertitel)"), which by itself doesn't say which language. Asks the broadcaster's own API
    /// first, then falls back to the configured original language - but only when the subscription
    /// asked for a fallback; "store as und" and "skip" both leave the placeholder in place, and the
    /// job builder acts on it later.
    /// </summary>
    /// <param name="subscription">The subscription the item belongs to.</param>
    /// <param name="videoInfo">The parsed item information to fill in.</param>
    /// <param name="item">The API result the information came from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the language has been filled in.</returns>
    private async Task SetOvLanguageIfSetAsync(Subscription subscription, VideoInfo? videoInfo, ResultItemDto item, CancellationToken cancellationToken)
    {
        if (videoInfo is null || !OriginalVersionLanguagePolicy.IsUndefined(videoInfo.Language))
        {
            return;
        }

        var resolvedLanguage = await _originalVersionLanguageResolver
            .TryGetOriginalVersionLanguageAsync(item.UrlWebsite, cancellationToken)
            .ConfigureAwait(false);

        var normalized = LanguageCodes.Normalize(resolvedLanguage);
        if (normalized is not null)
        {
            videoInfo.Language = normalized;
            return;
        }

        if (subscription.Metadata.UndefinedOriginalVersionHandling == UndefinedOriginalVersionHandling.UseFallbackLanguage)
        {
            var fallback = LanguageCodes.Normalize(GetConfiguredOriginalLanguage(subscription));
            if (fallback is not null)
            {
                videoInfo.Language = fallback;
            }
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

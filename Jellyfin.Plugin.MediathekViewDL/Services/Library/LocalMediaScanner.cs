using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Library;

/// <summary>
/// Service to scan local directories for existing episodes.
/// </summary>
public class LocalMediaScanner : ILocalMediaScanner
{
    private readonly ILogger<LocalMediaScanner> _logger;
    private readonly IVideoParser _videoParser;
    private readonly ILanguageDetectionService _languageDetectionService;

    // Supported video extensions
    private readonly string[] _videoExtensions = { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".strm", ".mka", ".webm" };

    // Extensions we write secondary audio tracks to. These sit *next to* a video file rather than
    // being an episode's own video, so they contribute their language to an episode without being
    // eligible as the file other tracks get attached to.
    private readonly string[] _sidecarAudioExtensions = { ".mka" };

    // Supported subtitle extensions
    private readonly string[] _subtitleExtensions = { ".vtt", ".ttml", ".srt" };

    // Supported info extensions
    private readonly string[] _infoExtensions = { ".txt", ".nfo" };

    // Walking a library tree is the expensive part of duplicate detection, and the same tree gets
    // walked repeatedly: in one real run /media/Serien (21440 files) was scanned three times and
    // /media/Filme three times, 30.6 of the 48.7 seconds spent scanning. Sonarr and Radarr avoid
    // this by never touching the disk on this path at all - they keep the library in a database
    // and reconcile it with an explicit rescan. That is a much larger change than this plugin
    // needs; remembering a result for the length of a run gets most of the benefit.
    //
    // The entry is keyed by series name as well as directory because the name is parsing context:
    // the same folder read on behalf of two subscriptions can yield different episode numbers.
    private readonly Dictionary<(string Directory, string SeriesName), (DateTime ScannedAt, LocalScanResult Result)> _cache = new();
    private readonly object _cacheLock = new();

    // How long a result may be reused. A subscription run is the thing this is meant to span, and
    // those take minutes; anything older is cheap enough to read again and not worth the risk of
    // acting on a stale picture of the library. DownloadScheduledTask additionally clears the whole
    // cache when a run starts, so a run never begins on top of what an earlier one saw.
    private static readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalMediaScanner"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="videoParser">The video parser.</param>
    /// <param name="languageDetectionService">The language detection service, used to recover the language of secondary-audio sidecars from their file name.</param>
    public LocalMediaScanner(ILogger<LocalMediaScanner> logger, IVideoParser videoParser, ILanguageDetectionService languageDetectionService)
    {
        _logger = logger;
        _videoParser = videoParser;
        _languageDetectionService = languageDetectionService;
    }

    /// <inheritdoc />
    public LocalEpisodeCache ScanDirectory(string directoryPath, string seriesName)
    {
        return GetOrScan(directoryPath, seriesName).EpisodeCache;
    }

    /// <inheritdoc />
    public LocalScanResult ScanSubscriptionDirectory(string directoryPath, string seriesName)
    {
        return GetOrScan(directoryPath, seriesName);
    }

    /// <inheritdoc />
    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    private LocalScanResult GetOrScan(string directoryPath, string seriesName)
    {
        var key = (directoryPath ?? string.Empty, seriesName ?? string.Empty);

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.ScannedAt < _cacheLifetime)
            {
                _logger.LogDebug("Reusing the scan of '{Path}' from {Age:0.0}s ago.", directoryPath, (DateTime.UtcNow - cached.ScannedAt).TotalSeconds);
                return cached.Result;
            }
        }

        // Deliberately outside the lock: a scan can take ten seconds or more on a large library,
        // and holding the lock across it would serialize every subscription behind whichever one
        // happened to scan first - the opposite of what this cache is for. Two callers racing on
        // the same directory both scan once and the second result wins, which costs one redundant
        // scan in a rare case instead of a stall in the common one.
        var result = ScanDirectoryInternal(directoryPath, seriesName);

        lock (_cacheLock)
        {
            _cache[key] = (DateTime.UtcNow, result);
        }

        return result;
    }

    private LocalScanResult ScanDirectoryInternal(string directoryPath, string seriesName)
    {
        var result = new LocalScanResult();

        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            _logger.LogDebug("Directory does not exist or is invalid: {Path}", directoryPath);
            return result;
        }

        try
        {
            _logger.LogInformation("Scanning local directory: {Path}", directoryPath);

            var files = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories).ToList();

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                var fileName = Path.GetFileNameWithoutExtension(file);

                if (_videoExtensions.Contains(extension))
                {
                    var videoInfo = _videoParser.ParseVideoInfo(seriesName, fileName);
                    var isSidecarAudio = _sidecarAudioExtensions.Contains(extension);

                    if (videoInfo != null)
                    {
                        // The parser only ever sees the name without its extension, so a track written
                        // as "Title.eng.mka" reaches it as "Title.eng" - and the language-suffix rule
                        // wants the language to be the *second to last* dot segment, which it no longer
                        // is. Re-running detection over the full file name restores that, so a sidecar
                        // reports the language it actually carries instead of silently defaulting to
                        // German (which would make us re-fetch it on every run).
                        videoInfo.Language = _languageDetectionService
                            .DetectLanguage(Path.GetFileName(file), videoInfo.Language)
                            .LanguageCode;
                    }

                    result.Files.Add(new ScannedFile
                    {
                        FilePath = file,
                        Type = extension == ".strm" ? FileType.Strm : FileType.Video,
                        VideoInfo = videoInfo
                    });

                    // Recorded for every media file, not just the ones the parser can pull an
                    // episode number out of: a film's title carries no numbering, so the indexes
                    // below never see it and duplicate detection was blind to entire film
                    // libraries.
                    result.EpisodeCache.AddFile(file);

                    if (videoInfo != null)
                    {
                        if ((videoInfo.SeasonNumber.HasValue && videoInfo.EpisodeNumber.HasValue) || videoInfo.AbsoluteEpisodeNumber.HasValue)
                        {
                            result.EpisodeCache.Add(videoInfo.SeasonNumber, videoInfo.EpisodeNumber, videoInfo.AbsoluteEpisodeNumber, file, videoInfo.Language, isSidecarAudio);
                        }
                    }
                }
                else if (_subtitleExtensions.Contains(extension))
                {
                    result.Files.Add(new ScannedFile
                    {
                        FilePath = file,
                        Type = FileType.Subtitle
                    });
                }
                else if (_infoExtensions.Contains(extension))
                {
                    result.Files.Add(new ScannedFile
                    {
                        FilePath = file,
                        Type = FileType.Info
                    });
                }
            }

            _logger.LogInformation(
                "Scan complete. Found {Total} total files, {MediaCount} media files, {SECount} S/E episodes and {AbsCount} absolute numbered episodes.",
                result.Files.Count,
                result.EpisodeCache.FileCount,
                result.EpisodeCache.SeasonEpisodeCount,
                result.EpisodeCache.AbsoluteEpisodeCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning directory: {Path}", directoryPath);
        }

        return result;
    }
}

using System;
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
        return ScanDirectoryInternal(directoryPath, seriesName).EpisodeCache;
    }

    /// <inheritdoc />
    public LocalScanResult ScanSubscriptionDirectory(string directoryPath, string seriesName)
    {
        return ScanDirectoryInternal(directoryPath, seriesName);
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
                "Scan complete. Found {Total} total files, {SECount} S/E episodes and {AbsCount} absolute numbered episodes.",
                result.Files.Count,
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

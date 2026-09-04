using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Clients;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <inheritdoc/>
public class UndefinedAudioLanguageBackfill : IUndefinedAudioLanguageBackfill
{
    private const string UndefinedSuffix = ".und.mka";

    private readonly IFFmpegService _ffmpegService;
    private readonly ILogger<UndefinedAudioLanguageBackfill> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UndefinedAudioLanguageBackfill"/> class.
    /// </summary>
    /// <param name="ffmpegService">The ffmpeg service.</param>
    /// <param name="logger">The logger.</param>
    public UndefinedAudioLanguageBackfill(IFFmpegService ffmpegService, ILogger<UndefinedAudioLanguageBackfill> logger)
    {
        _ffmpegService = ffmpegService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> BackfillAsync(string? directory, string? languageCode, bool recursive, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || OriginalVersionLanguagePolicy.IsUndefined(languageCode)
            || !Directory.Exists(directory))
        {
            return 0;
        }

        var language = languageCode!.Trim();
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(
                directory,
                "*" + UndefinedSuffix,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not scan '{Directory}' for undetermined audio tracks.", directory);
            return 0;
        }

        var updated = 0;
        foreach (var source in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = source[..^UndefinedSuffix.Length] + "." + language + ".mka";
            if (File.Exists(destination))
            {
                // The track already exists in the real language - this is a leftover from an earlier
                // run. Leave it alone rather than silently deleting a file the user still has.
                _logger.LogInformation(
                    "Leaving '{Source}' as is: '{Destination}' already exists.",
                    source,
                    destination);
                continue;
            }

            if (await RetagAsync(source, destination, language, cancellationToken).ConfigureAwait(false))
            {
                updated++;
            }
        }

        if (updated > 0)
        {
            _logger.LogInformation(
                "Filled in the language '{Language}' for {Count} previously undetermined audio track(s) in '{Directory}'.",
                language,
                updated,
                directory);
        }

        return updated;
    }

    /// <summary>
    /// Rewrites one file, writing the language into the file itself and only then removing the
    /// original - a failed remux must never cost the user the track they already had.
    /// </summary>
    private async Task<bool> RetagAsync(string source, string destination, string language, CancellationToken cancellationToken)
    {
        var temporary = destination + ".mvdl-tmp";

        try
        {
            var success = await _ffmpegService
                .RetagAudioLanguageAsync(source, temporary, language, setOriginalLanguageTag: true, cancellationToken)
                .ConfigureAwait(false);

            if (!success || !File.Exists(temporary) || new FileInfo(temporary).Length == 0)
            {
                _logger.LogWarning("Could not rewrite '{Source}' with language '{Language}'; leaving it unchanged.", source, language);
                return false;
            }

            File.Move(temporary, destination);
            File.Delete(source);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not replace '{Source}' with '{Destination}'.", source, destination);
            return false;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not delete the temporary file '{Path}'.", temporary);
                }
            }
        }
    }
}

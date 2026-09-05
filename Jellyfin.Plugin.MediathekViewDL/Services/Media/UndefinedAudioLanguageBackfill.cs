using System;
using System.Globalization;
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

    /// <inheritdoc/>
    public async Task<bool> BackfillEpisodeAsync(string? videoPath, string? languageCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || LanguageCodes.Normalize(languageCode) is not { } language)
        {
            return false;
        }

        var source = Path.ChangeExtension(videoPath, null) + UndefinedSuffix;
        var destination = Path.ChangeExtension(videoPath, null) + "." + language + ".mka";

        if (!File.Exists(source) || File.Exists(destination))
        {
            return false;
        }

        return await RetagAsync(source, destination, language, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rewrites one file, writing the language into the file itself and only then removing the
    /// original - a failed remux must never cost the user the track they already had.
    /// </summary>
    private async Task<bool> RetagAsync(string source, string destination, string language, CancellationToken cancellationToken)
    {
        // Unique per attempt. Two subscription passes can walk the same library at the same time -
        // in the log that prompted this, both retagged the same episode a tenth of a second apart -
        // and a shared temporary path means one pass moves the file away while the other is still
        // writing to it, which surfaced as a FileNotFoundException on the move.
        var temporary = destination + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".mvdl-tmp";

        try
        {
            if (!File.Exists(source) || File.Exists(destination))
            {
                // A concurrent pass got here first. Not an error: the library ends up as intended.
                return false;
            }

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
            if (File.Exists(destination) && !File.Exists(source))
            {
                // The check above cannot close this window - the other pass can finish at any point
                // while this one runs ffmpeg. The outcome is the one that was wanted either way.
                _logger.LogDebug(ex, "'{Destination}' was already written by a concurrent pass.", destination);
                return false;
            }

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

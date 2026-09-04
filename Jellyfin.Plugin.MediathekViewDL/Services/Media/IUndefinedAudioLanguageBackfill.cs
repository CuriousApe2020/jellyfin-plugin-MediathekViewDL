using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Fills in the language of secondary audio files that were stored as "und" before the language was
/// known - e.g. when a subscription first ran with "allow undefined original versions" and an
/// original language was configured only later.
/// </summary>
public interface IUndefinedAudioLanguageBackfill
{
    /// <summary>
    /// Renames every "*.und.mka" below the given directory to the given language and writes that
    /// language into the file itself, without re-encoding.
    /// </summary>
    /// <param name="directory">The directory to walk.</param>
    /// <param name="languageCode">The language to fill in.</param>
    /// <param name="recursive">Whether to include subdirectories - true for a subscription's own
    /// folder, false for the single folder a manual download landed in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of files that were updated.</returns>
    Task<int> BackfillAsync(string? directory, string? languageCode, bool recursive, CancellationToken cancellationToken);
}

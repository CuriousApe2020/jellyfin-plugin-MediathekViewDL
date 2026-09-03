namespace Jellyfin.Plugin.MediathekViewDL.Services.Library;

/// <summary>
/// Interface for the LocalMediaScanner service.
/// </summary>
public interface ILocalMediaScanner
{
    /// <summary>
    /// Scans the specified directory for video files and builds a cache of existing episodes.
    /// </summary>
    /// <param name="directoryPath">The path to the directory to scan.</param>
    /// <param name="seriesName">The name of the series (used for parsing context).</param>
    /// <returns>A <see cref="LocalEpisodeCache"/> containing the found episodes.</returns>
    LocalEpisodeCache ScanDirectory(string directoryPath, string seriesName);

    /// <summary>
    /// Performs an extended scan of the specified directory, including subtitles and info files.
    /// </summary>
    /// <param name="directoryPath">The path to the directory to scan.</param>
    /// <param name="seriesName">The name of the series (used for parsing context).</param>
    /// <returns>A <see cref="LocalScanResult"/> containing all found files and the episode cache.</returns>
    LocalScanResult ScanSubscriptionDirectory(string directoryPath, string seriesName);

    /// <summary>
    /// Drops everything remembered from earlier scans, so the next one reads the disk again.
    /// </summary>
    /// <remarks>
    /// Called at the start of a subscription run. Results are otherwise reused for a short while
    /// (see the implementation), which is what keeps one run from walking the same library tree
    /// over and over - but a run must never start on top of what an earlier one saw.
    /// </remarks>
    void InvalidateCache();
}

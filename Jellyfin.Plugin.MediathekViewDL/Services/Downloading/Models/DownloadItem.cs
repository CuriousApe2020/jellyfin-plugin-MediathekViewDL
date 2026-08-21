namespace Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Models;

/// <summary>
/// Represents a single download item.
/// </summary>
public class DownloadItem
{
    /// <summary>
    /// Gets or sets the source URL (Video URL, Subtitle URL, etc.).
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full local path where the result should be saved.
    /// </summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path of an existing file that this download is intended to replace (e.g., during a quality upgrade).
    /// </summary>
    public string? ReplaceFilePath { get; set; }

    /// <summary>
    /// Gets or sets the type of operation to perform.
    /// </summary>
    public DownloadType JobType { get; set; }

    /// <summary>
    /// Gets or sets the language code for this item (used by AudioExtraction for standalone
    /// secondary-audio files, e.g. when several differently-tagged tracks are queued in one job).
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this audio track is an audio description
    /// (used by AudioExtraction to set the correct ffmpeg disposition, e.g. "visual_impaired").
    /// </summary>
    public bool IsAudioDescription { get; set; }
}

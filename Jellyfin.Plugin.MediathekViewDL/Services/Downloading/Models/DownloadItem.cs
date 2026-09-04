using System.Collections.Generic;

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
    /// Gets or sets other quality-tier URLs for the same item, ordered best-first, to fall back to
    /// if <see cref="SourceUrl"/> fails its pre-download validation. A job can sit in the download
    /// queue (currently strictly one-at-a-time) for a while after the URL was resolved and
    /// validated at discovery time - broadcaster CDN URLs are often time-limited, so by the time
    /// execution actually starts, the previously-valid <see cref="SourceUrl"/> can have expired even
    /// though a lower-quality sibling from the same search result is still reachable. Only populated
    /// for the main video item, where the broadcaster actually offers multiple quality tiers to fall
    /// back to; left null/empty for everything else (subtitles, secondary-audio tracks) since those
    /// only ever have the one URL.
    /// </summary>
    public IReadOnlyList<string>? FallbackSourceUrls { get; set; }

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

    /// <summary>
    /// Gets or sets a value indicating whether the audio track's title/handler metadata should be
    /// replaced with a clean, generated label (see <see cref="Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings.BaseDownloadSettings.CleanAudioTrackLabels"/>).
    /// </summary>
    public bool CleanAudioTrackLabel { get; set; }

    /// <summary>
    /// Gets or sets the MediathekViewWeb result ID this item was sourced from, if it came from its own
    /// distinct search result rather than being derived from the main item's URL (e.g. a sibling row
    /// grouped in via <see cref="Media.AudioVariantGroupingService"/>). When set, download-history
    /// recording uses this ID instead of the job's own <see cref="Downloading.Models.DownloadJob.ItemId"/>,
    /// so the sibling row isn't re-offered as a fresh, ungrouped item on the next subscription run.
    /// Left null for items derived from the main URL (nothing to record separately) and for
    /// subtitle/main-video items (already covered by the job's own ItemId).
    /// </summary>
    public string? SourceItemId { get; set; }
}

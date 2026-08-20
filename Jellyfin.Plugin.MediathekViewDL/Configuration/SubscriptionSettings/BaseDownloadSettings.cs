namespace Jellyfin.Plugin.MediathekViewDL.Configuration.SubscriptionSettings;

/// <summary>
/// Base settings for the download process.
/// </summary>
public record BaseDownloadSettings
{
    /// <summary>
    /// Gets a value indicating whether to use streaming URL files (.strm) instead of downloading the actual video files.
    /// </summary>
    public bool UseStreamingUrlFiles { get; init; }

    /// <summary>
    /// Gets a value indicating whether to download the full video for secondary audio languages.
    /// If false, only the audio track will be extracted for secondary languages.
    /// </summary>
    public bool DownloadFullVideoForSecondaryAudio { get; init; }

    /// <summary>
    /// Gets a value indicating whether to detect and download secondary audio tracks (original version,
    /// audio description, "klare Sprache") that MediathekViewWeb's search index doesn't surface as a
    /// separate result - derived directly from the main video's URL. Downloaded as separate files next
    /// to the main video, the same way a secondary-language item found via the API is handled.
    /// </summary>
    public bool DetectUndetectedSecondaryAudio { get; init; }

    /// <summary>
    /// Gets a value indicating whether to download a detected original-version (different-language) audio
    /// track. Only relevant when <see cref="DetectUndetectedSecondaryAudio"/> is enabled.
    /// </summary>
    public bool DownloadOriginalVersionAudio { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to download a detected audio-description track (narrated audio for
    /// visually impaired viewers, same language as the main track). Only relevant when
    /// <see cref="DetectUndetectedSecondaryAudio"/> is enabled.
    /// </summary>
    public bool DownloadAudioDescriptionAudio { get; init; }

    /// <summary>
    /// Gets a value indicating whether to download a detected "klare Sprache" (speech-optimized) audio
    /// track, same language as the main track. Only relevant when <see cref="DetectUndetectedSecondaryAudio"/>
    /// is enabled.
    /// </summary>
    public bool DownloadClearSpeechAudio { get; init; }

    /// <summary>
    /// Gets a value indicating whether to resolve the real spoken language of an "Originalversion"
    /// audio track via the broadcaster's own API (currently ARD's page-gateway API and arte's
    /// player-config API), instead of tagging it with the generic "und" placeholder. Applies both to
    /// tracks found via <see cref="DetectUndetectedSecondaryAudio"/> and to items MediathekViewWeb's
    /// search API already returns as a distinct, title-marked original-version result.
    /// </summary>
    public bool ResolveOriginalVersionLanguage { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to build a clean audio track label from language, codec, and
    /// channel info (e.g. "English - AAC - 7.1") and clear the broadcaster's own embedded title/handler
    /// metadata (e.g. "Hessischer Rundfunk mp4toolbox 1.17.1"), instead of leaving that metadata as-is.
    /// Applies to both the main video's audio track and any standalone secondary-audio files.
    /// </summary>
    public bool CleanAudioTrackLabels { get; init; }

    /// <summary>
    /// Gets a value indicating whether to allow falling back to lower quality versions
    /// if HD version is not available.
    /// </summary>
    public bool AllowFallbackToLowerQuality { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to check if the URL retrieved from MediathekViewWeb API is valid.
    /// If not it will try with the next lower quality available.
    /// This can slow down the Scan. Especially if thers a lot of unavailable videos.
    /// </summary>
    public bool QualityCheckWithUrl { get; init; }

    /// <summary>
    /// Gets a value indicating whether to always create a subfolder for the subscription (using the subscription name),
    /// even if the content is a movie and the global setting 'UseTopicForMoviePath' is disabled.
    /// </summary>
    public bool AlwaysCreateSubfolder { get; init; }

    /// <summary>
    /// Gets a value indicating whether to enable enhanced duplicate detection.
    /// If enabled, the target directory is scanned for existing files matching the season/episode pattern.
    /// </summary>
    public bool EnhancedDuplicateDetection { get; init; }
}

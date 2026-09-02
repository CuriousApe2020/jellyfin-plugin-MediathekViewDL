using System.Xml.Serialization;

namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

/// <summary>
/// Base settings for the download process.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkBaseDownloadSettings")]
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
    /// Gets a value indicating whether to detect and merge sibling MediathekViewWeb search results that
    /// represent the same episode with a different audio track (foreign-language original version,
    /// audio description, or "klare Sprache") into one download job with multiple audio tracks, instead
    /// of downloading them as separate items that collide on the same destination path. Confirmed
    /// necessary for arte (crawled once per channel variant, e.g. "ARTE.DE"/"ARTE.FR", plus separate
    /// rows for markers like "(Originalversion)"/"(Audiodeskription)") and for ZDF/ZDFneo/3sat (crawled
    /// once per language, with the language named directly in the title, e.g. "(Englisch)"). Independent
    /// from <see cref="DetectUndetectedSecondaryAudio"/>, which derives variants from a single URL's own
    /// tokens (ARD) rather than from other search results - the two can be enabled together.
    /// </summary>
    public bool DetectCrossResultAudioVariants { get; init; }

    /// <summary>
    /// Gets a value indicating whether a newly found audio variant of an episode that already exists
    /// locally should be attached to that existing video as an extra audio track, instead of being
    /// downloaded as a second, near-duplicate video file.
    /// </summary>
    /// <remarks>
    /// Duplicate detection is language-aware, so an episode present only in one language does not
    /// suppress the same episode in another - which is what produces two video files for one episode
    /// and, in Jellyfin, two entries where the user expects one with selectable audio. With this
    /// enabled the second language is written next to the existing video as a ".mka" track instead.
    /// The existing video file itself is never touched or rewritten. Requires
    /// <see cref="EnhancedDuplicateDetection"/>, which is what scans the library in the first place.
    /// </remarks>
    public bool AddAudioToExistingEpisodes { get; init; }

    /// <summary>
    /// Gets a value indicating whether to download a detected original-version (different-language) audio
    /// track. Only relevant when <see cref="DetectUndetectedSecondaryAudio"/> or
    /// <see cref="DetectCrossResultAudioVariants"/> is enabled.
    /// </summary>
    public bool DownloadOriginalVersionAudio { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to download a detected audio-description track (narrated audio for
    /// visually impaired viewers, same language as the main track). Only relevant when
    /// <see cref="DetectUndetectedSecondaryAudio"/> or <see cref="DetectCrossResultAudioVariants"/> is enabled.
    /// </summary>
    public bool DownloadAudioDescriptionAudio { get; init; }

    /// <summary>
    /// Gets a value indicating whether to download a detected "klare Sprache" (speech-optimized) audio
    /// track, same language as the main track. Only relevant when <see cref="DetectUndetectedSecondaryAudio"/>
    /// or <see cref="DetectCrossResultAudioVariants"/> is enabled.
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

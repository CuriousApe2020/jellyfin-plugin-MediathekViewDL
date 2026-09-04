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
    /// Gets which language versions are stored at all - every one that is found, or only the ones
    /// listed in <see cref="SelectedAudioLanguages"/>.
    /// </summary>
    public AudioLanguageMode AudioLanguageMode { get; init; } = AudioLanguageMode.All;

    /// <summary>
    /// Gets the languages to keep when <see cref="AudioLanguageMode"/> is
    /// <see cref="AudioLanguageMode.Selected"/>: ISO 639 codes separated by commas (e.g.
    /// "deu, eng"). Two- and three-letter codes are both accepted and normalized to the
    /// three-letter form; an empty list keeps nothing and is treated as "no filter configured".
    /// </summary>
    public string? SelectedAudioLanguages { get; init; }

    /// <summary>
    /// Gets a value indicating whether to download the full video for secondary audio languages.
    /// If false, only the audio track will be extracted for secondary languages.
    /// </summary>
    public bool DownloadFullVideoForSecondaryAudio { get; init; }

    /// <summary>
    /// Gets a value indicating whether to detect additional *language* versions that MediathekViewWeb's
    /// search index doesn't surface as a separate result - derived directly from the main video's URL.
    /// Downloaded as separate files next to the main video, the same way a secondary-language item found
    /// via the API is handled. Audio description and "klare Sprache" have their own, separate switch in
    /// <see cref="AccessibilitySettings.DetectUndetectedAudioDescription"/> and
    /// <see cref="AccessibilitySettings.DetectUndetectedClearSpeech"/>.
    /// </summary>
    public bool DetectUndetectedSecondaryAudio { get; init; }

    /// <summary>
    /// Gets a value indicating whether to detect and merge sibling MediathekViewWeb search results that
    /// represent the same episode in a different language into one download job with multiple audio
    /// tracks, instead of downloading them as separate items that collide on the same destination path.
    /// Confirmed
    /// necessary for arte (crawled once per channel variant, e.g. "ARTE.DE"/"ARTE.FR") and for
    /// ZDF/ZDFneo/3sat (crawled once per language, with the language named directly in the title, e.g.
    /// "(Englisch)"). Independent from <see cref="DetectUndetectedSecondaryAudio"/>, which derives
    /// variants from a single URL's own tokens (ARD) rather than from other search results - the two can
    /// be enabled together. Audio description and "klare Sprache" have their own, separate switch in
    /// <see cref="AccessibilitySettings.DetectCrossResultAudioDescription"/> and
    /// <see cref="AccessibilitySettings.DetectCrossResultClearSpeech"/>.
    /// </summary>
    public bool DetectCrossResultAudioVariants { get; init; }

    /// <summary>
    /// Gets a value indicating whether a newly found language version of an episode that already
    /// exists locally is attached to that existing video as an extra audio track, instead of being
    /// downloaded as a second, near-duplicate video file. The existing video file itself is never
    /// touched or rewritten.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="EnhancedDuplicateDetection"/>: that is what reads the library in the first
    /// place, and without it the plugin cannot know an episode is already there. The settings UI
    /// refuses to save the combination "on without duplicate detection" rather than letting it fail
    /// silently at download time.
    /// </remarks>
    public bool AddAudioToExistingEpisodes { get; init; }

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

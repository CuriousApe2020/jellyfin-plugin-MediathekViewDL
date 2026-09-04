using System.Xml.Serialization;

namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

/// <summary>
/// Settings for accessibility features.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkAccessibilitySettings")]
public record AccessibilitySettings
{
    /// <summary>
    /// Gets a value indicating whether to allow downloading versions with audio descriptions.
    /// </summary>
    public bool AllowAudioDescription { get; init; }

    /// <summary>
    /// Gets a value indicating whether to download versions with "klare Sprache" (speech-optimized
    /// audio). Off by default, like audio description: such versions used to come in as ordinary
    /// items whenever the search happened to return them, which is not something to keep doing
    /// silently.
    /// </summary>
    public bool DownloadClearSpeech { get; init; }

    /// <summary>
    /// Gets a value indicating whether to detect audio-description and "klare Sprache" tracks that
    /// MediathekViewWeb's search index doesn't surface as a separate result - derived from the main
    /// video's URL. The accessibility counterpart to
    /// <see cref="BaseDownloadSettings.DetectUndetectedSecondaryAudio"/>, kept separate so a
    /// subscription can collect language versions without collecting accessibility tracks, or the
    /// other way round. Applies to both <see cref="AllowAudioDescription"/> and
    /// <see cref="DownloadClearSpeech"/>.
    /// </summary>
    public bool DetectUndetectedAccessibilityAudio { get; init; }

    /// <summary>
    /// Gets a value indicating whether to merge sibling search results that represent the same
    /// episode with audio description or "klare Sprache" into the episode's download job as an extra
    /// audio track. The accessibility counterpart to
    /// <see cref="BaseDownloadSettings.DetectCrossResultAudioVariants"/>. Applies to both
    /// <see cref="AllowAudioDescription"/> and <see cref="DownloadClearSpeech"/>.
    /// </summary>
    public bool DetectCrossResultAccessibilityVariants { get; init; }

    /// <summary>
    /// Gets a value indicating whether a newly found audio-description or "klare Sprache" track for
    /// an episode that already exists locally is attached to that existing video as an extra audio
    /// track, instead of being downloaded as a second, near-duplicate video file. The accessibility
    /// counterpart to <see cref="BaseDownloadSettings.AddAudioToExistingEpisodes"/>, and like it,
    /// requires <see cref="BaseDownloadSettings.EnhancedDuplicateDetection"/>.
    /// </summary>
    public bool AddAccessibilityAudioToExistingEpisodes { get; init; }

    /// <summary>
    /// Gets a value indicating whether to allow downloading versions with sign language (Gebärdensprache).
    /// </summary>
    public bool AllowSignLanguage { get; init; }

    /// <summary>
    /// Gets the audio-track language(s) an item must have - in addition to its normal main track - for
    /// it to be downloaded at all: ISO 639 codes separated by commas (e.g. "eng" or "eng, fra"), where
    /// any one of them is enough. Checked against every audio track
    /// the item ends up with: the main track's own language, any secondary track derived from the
    /// main video's URL (see <see cref="BaseDownloadSettings.DetectUndetectedSecondaryAudio"/>), and
    /// any secondary track merged in from a sibling search result (see
    /// <see cref="BaseDownloadSettings.DetectCrossResultAudioVariants"/>). Null/empty disables the
    /// filter (the default): every matching item is downloaded regardless of which audio tracks it
    /// has, exactly as before this setting existed. When set, an item that ends up with no track in
    /// this language is skipped entirely - the main track is not downloaded on its own either. Only
    /// takes effect for tracks the relevant detection settings above are actually enabled to look
    /// for - this setting does not turn on detection or original-version-language resolution itself.
    /// </summary>
    public string? RequiredAudioLanguage { get; init; }
}

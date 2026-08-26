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
    /// Gets a value indicating whether to allow downloading versions with sign language (Gebärdensprache).
    /// </summary>
    public bool AllowSignLanguage { get; init; }

    /// <summary>
    /// Gets the audio-track language (3-letter ISO code, e.g. "eng") an item must have - in addition
    /// to its normal main track - for it to be downloaded at all. Checked against every audio track
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

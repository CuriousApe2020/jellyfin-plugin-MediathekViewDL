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
}

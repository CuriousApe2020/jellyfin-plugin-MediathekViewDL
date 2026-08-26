using System.Xml.Serialization;

namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.Groups;

/// <summary>
/// Options for Live TV.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkLiveTvOptions")]
public record LiveTvOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether subtitles should be enabled for Zapp Live TV.
    /// </summary>
    public bool EnableZappSubtitles { get; set; } = false;
}

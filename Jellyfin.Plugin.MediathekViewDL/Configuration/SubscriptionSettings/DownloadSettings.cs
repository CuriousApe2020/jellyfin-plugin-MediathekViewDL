using System.Xml.Serialization;

namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

/// <summary>
/// Settings for the download process within a subscription.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkDownloadSettings")]
public record DownloadSettings : BaseDownloadSettings
{
    /// <summary>
    /// Gets the specific download path for this subscription. Overrides the default path if set.
    /// </summary>
    public string DownloadPath { get; init; } = string.Empty;
}

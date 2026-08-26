using System.Xml.Serialization;
using Jellyfin.Plugin.MediathekViewDL.Configuration.SubscriptionSettings;

namespace Jellyfin.Plugin.MediathekViewDL.Configuration.Groups;

/// <summary>
/// Default values for new subscriptions.
/// Currently without function.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="Configuration.PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkSubscriptionDefaults")]
public record SubscriptionDefaults
{
    /// <summary>
    /// Gets the default download settings.
    /// </summary>
    public BaseDownloadSettings DownloadSettings { get; init; } = new();

    /// <summary>
    /// Gets the default metadata settings.
    /// </summary>
    public MetadataSettings MetadataSettings { get; init; } = new();

    /// <summary>
    /// Gets the default search settings.
    /// </summary>
    public BaseSearchSettings SearchSettings { get; init; } = new();

    /// <summary>
    /// Gets the default series settings.
    /// </summary>
    public SeriesSettings SeriesSettings { get; init; } = new();

    /// <summary>
    /// Gets the default accessibility settings.
    /// </summary>
    public AccessibilitySettings AccessibilitySettings { get; init; } = new();
}

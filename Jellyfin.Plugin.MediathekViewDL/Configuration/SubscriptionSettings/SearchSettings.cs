using System.Collections.ObjectModel;
using System.Xml.Serialization;
using Jellyfin.Plugin.MediathekViewDL.Api.Models;

namespace Jellyfin.Plugin.MediathekViewDL.Configuration.SubscriptionSettings;

/// <summary>
/// Settings for searching the Mediathek within a subscription.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="Configuration.PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkSearchSettings")]
public record SearchSettings : BaseSearchSettings
{
    /// <summary>
    /// Gets the search criteria for the MediathekViewWeb API.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlArrayItemAttribute"/> pins the per-item XML element tag to "QueryFieldsDto" -
    /// the item element name that XmlSerializer defaulted to before <see cref="QueryFieldsDto"/> got
    /// its own explicit, collision-avoiding <see cref="XmlTypeAttribute"/> (which would otherwise
    /// change the default item tag and make existing saved search criteria unreadable).
    /// </remarks>
    [XmlArrayItem(ElementName = "QueryFieldsDto")]
    public Collection<QueryFieldsDto> Criteria { get; init; } = new();
}

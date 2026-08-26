using System.Collections.ObjectModel;
using System.Xml.Serialization;
using Jellyfin.Plugin.MediathekViewDL.Api.Models.Enums;

namespace Jellyfin.Plugin.MediathekViewDL.Api.Models;

/// <summary>
/// Defines a filter for the search.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="Configuration.PluginConfiguration"/> for the full explanation.
/// This type is reachable from the plugin configuration via
/// <see cref="Configuration.SubscriptionSettings.SearchSettings.Criteria"/>.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkQueryFieldsDto")]
public record QueryFieldsDto
{
    /// <summary>
    /// Gets the fields to search in.
    /// </summary>
    public Collection<QueryFieldType> Fields { get; init; } = new();

    /// <summary>
    /// Gets or sets the search query.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is an exclusion filter (NOT).
    /// </summary>
    public bool IsExclude { get; set; }
}

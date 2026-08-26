using System;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.MediathekViewDL.Configuration.SubscriptionSettings;

/// <summary>
/// Base settings for searching the Mediathek.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="Configuration.PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkBaseSearchSettings")]
public record BaseSearchSettings
{
    /// <summary>
    /// Gets the minimum duration in minutes for search results.
    /// </summary>
    public int? MinDurationMinutes { get; init; }

    /// <summary>
    /// Gets the maximum duration in minutes for search results.
    /// </summary>
    public int? MaxDurationMinutes { get; init; }

    /// <summary>
    /// Gets the minimum broadcast date for search results.
    /// </summary>
    public DateTimeOffset? MinBroadcastDate { get; init; }

    /// <summary>
    /// Gets the maximum broadcast date for search results.
    /// </summary>
    public DateTimeOffset? MaxBroadcastDate { get; init; }
}

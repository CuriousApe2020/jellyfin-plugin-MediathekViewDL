using System.Xml.Serialization;

namespace Jellyfin.Plugin.MediathekViewDL.Configuration.Groups;

/// <summary>
/// Options for maintenance and system behavior.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="Configuration.PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkMaintenanceOptions")]
public record MaintenanceOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to enable the automated cleanup of invalid .strm files.
    /// </summary>
    public bool EnableStrmCleanup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether downloads should be allowed if the available disk space cannot be determined.
    /// This can happen with network shares or non-standard file systems.
    /// </summary>
    public bool AllowDownloadOnUnknownDiskSpace { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed duration difference for file adoption matching.
    /// </summary>
    public System.TimeSpan AdoptionDurationThreshold { get; set; } = System.TimeSpan.FromSeconds(15);
}

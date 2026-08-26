using System.Xml.Serialization;

namespace Jellyfin.Plugin.MediathekViewDL.Configuration.SubscriptionSettings;

/// <summary>
/// Settings for metadata and file naming.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="Configuration.PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkMetadataSettings")]
public record MetadataSettings
{
    /// <summary>
    /// Gets a value indicating whether to create a local .nfo file with metadata (Episode number, description, etc.).
    /// </summary>
    public bool CreateNfo { get; init; } = false;

    /// <summary>
    /// Gets the language code (3-letter ISO) to use when the language cannot be detected or is detected as "und" (e.g. for OV content).
    /// </summary>
    public string? OriginalLanguage { get; init; }

    /// <summary>
    /// Gets a value indicating whether to append the broadcast date to the title.
    /// Useful for shows that don't have unique titles or season/episode numbers.
    /// </summary>
    public bool AppendDateToTitle { get; init; }

    /// <summary>
    /// Gets a value indicating whether to append the broadcast time to the title.
    /// Useful for shows that air multiple times a day (e.g. news).
    /// </summary>
    public bool AppendTimeToTitle { get; init; }

    /// <summary>
    /// Gets a value indicating whether to keep the original title without any automatic cleanup (e.g. removing features, date/time or language info).
    /// </summary>
    public bool KeepOriginalTitle { get; init; }
}

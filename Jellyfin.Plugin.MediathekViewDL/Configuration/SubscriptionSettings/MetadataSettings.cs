using System.Xml.Serialization;

namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

/// <summary>
/// Settings for metadata and file naming.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkMetadataSettings")]
public record MetadataSettings
{
    /// <summary>
    /// Gets a value indicating whether to create a local .nfo file with metadata (Episode number, description, etc.).
    /// </summary>
    public bool CreateNfo { get; init; } = false;

    /// <summary>
    /// Gets the language to fall back to for an original-version track whose spoken language nothing
    /// names - used when the handling below is set to "use the fallback language". A 3-letter ISO
    /// code such as "eng". Empty means "take the global default for new
    /// subscriptions"; if that is empty too, the track is stored as "und" instead.
    /// </summary>
    /// <remarks>
    /// Shown in the subscription editor under "Sprachfassungen" in the Download tab, next to the
    /// setting it belongs to - it is only stored here because that is where it has always lived, and
    /// moving it would silently drop what users already configured.
    /// </remarks>
    public string? OriginalLanguage { get; init; }

    /// <summary>
    /// Gets what happens to an original-version track whose language nothing names: tag it with
    /// <see cref="OriginalLanguage"/>, store it as "und", or skip it. Defaults to storing it as
    /// "und", which is how the plugin behaved before this setting existed.
    /// </summary>
    public UndefinedOriginalVersionHandling UndefinedOriginalVersionHandling { get; init; }
        = UndefinedOriginalVersionHandling.StoreAsUndetermined;

    /// <summary>
    /// Gets a value indicating whether audio tracks already stored as "und" are renamed and re-tagged
    /// once their real language becomes known - either because a language was configured, or because
    /// the broadcaster named it on a later run (a repeat broadcast that finally carries a proper
    /// code). Defaults to true.
    /// </summary>
    public bool BackfillAudioLanguages { get; init; } = true;

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

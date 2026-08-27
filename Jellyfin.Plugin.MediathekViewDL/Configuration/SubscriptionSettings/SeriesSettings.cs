using System.Xml.Serialization;

namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

/// <summary>
/// Settings for series parsing and handling.
/// </summary>
/// <remarks>
/// <see cref="XmlTypeAttribute"/> is set explicitly to avoid an XmlSerializer type-mapping
/// collision with the upstream plugin's identically-named type when both are loaded side by
/// side - see the remarks on <see cref="PluginConfiguration"/> for the full explanation.
/// </remarks>
[XmlType(TypeName = "MediathekViewDLForkSeriesSettings")]
public record SeriesSettings
{
    /// <summary>
    /// Gets a value indicating whether to only download content where season and episode can be parsed from the title.
    /// </summary>
    public bool EnforceSeriesParsing { get; init; }

    /// <summary>
    /// Gets a value indicating whether to skip content recognized as a series episode (the
    /// opposite of <see cref="EnforceSeriesParsing"/>) - for subscriptions meant to catch
    /// standalone films/specials that shouldn't accidentally also pull in an episode of an
    /// unrelated series matching the same search criteria. If both this and
    /// <see cref="EnforceSeriesParsing"/> are enabled at once, nothing will ever match, since
    /// every item would need to be recognized as a series episode and not be one at the same
    /// time; the checkbox for whichever was already on gets disabled while the other is checked
    /// to prevent that combination.
    /// </summary>
    public bool ExcludeSeries { get; init; }

    /// <summary>
    /// Gets a value indicating whether to allow downloading content with absolute episode numbering.
    /// This is ignored if EnforceSeriesParsing is false.
    /// </summary>
    public bool AllowAbsoluteEpisodeNumbering { get; init; }

    /// <summary>
    /// Gets a value indicating whether to treat videos that are not recognized as episodes as extras.
    /// This Option is ignored if EnforceSeriesParsing is true.
    /// </summary>
    public bool TreatNonEpisodesAsExtras { get; init; }

    /// <summary>
    /// Gets a value indicating whether trailers should be saved (only if TreatNonEpisodesAsExtras is true).
    /// </summary>
    public bool SaveTrailers { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether interviews should be saved (only if TreatNonEpisodesAsExtras is true).
    /// </summary>
    public bool SaveInterviews { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether generic extras (not trailers/interviews) should be saved (only if TreatNonEpisodesAsExtras is true).
    /// </summary>
    public bool SaveGenericExtras { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether extras should be saved as .strm files.
    /// </summary>
    public bool SaveExtrasAsStrm { get; init; }
}

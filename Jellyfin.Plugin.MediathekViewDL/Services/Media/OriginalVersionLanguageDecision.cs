namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// The outcome of <see cref="OriginalVersionLanguagePolicy.Decide"/>: either a language code the
/// track should be tagged with, or a refusal with the reason to report.
/// </summary>
/// <param name="LanguageCode">The language code to tag the track with, or null when refused.</param>
/// <param name="RefusalReason">Why the track must not be downloaded, or null when it may be.</param>
public sealed record OriginalVersionLanguageDecision(string? LanguageCode, string? RefusalReason)
{
    /// <summary>
    /// Gets a value indicating whether the track must not be downloaded.
    /// </summary>
    public bool IsRefused => RefusalReason is not null;

    /// <summary>
    /// Creates a decision that tags the track with the given language.
    /// </summary>
    /// <param name="languageCode">The language code to use.</param>
    /// <returns>The decision.</returns>
    public static OriginalVersionLanguageDecision Tag(string languageCode) => new(languageCode, null);

    /// <summary>
    /// Creates a decision that refuses the track.
    /// </summary>
    /// <param name="reason">The reason to report to the user.</param>
    /// <returns>The decision.</returns>
    public static OriginalVersionLanguageDecision Refuse(string reason) => new(null, reason);
}

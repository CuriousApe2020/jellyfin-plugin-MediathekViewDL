namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// The outcome of <see cref="OriginalVersionLanguagePolicy.Decide"/>: either the language code the
/// track is tagged with, or the reason it is not stored at all.
/// </summary>
/// <param name="LanguageCode">The language code to tag the track with, or null when it is skipped.</param>
/// <param name="SkipReason">Why the track is not stored, or null when it is.</param>
public sealed record OriginalVersionLanguageDecision(string? LanguageCode, string? SkipReason)
{
    /// <summary>
    /// Gets a value indicating whether the track is not stored.
    /// </summary>
    public bool IsSkipped => LanguageCode is null;

    /// <summary>
    /// Creates a decision that tags the track with the given language.
    /// </summary>
    /// <param name="languageCode">The language code to use.</param>
    /// <returns>The decision.</returns>
    public static OriginalVersionLanguageDecision Tag(string languageCode) => new(languageCode, null);

    /// <summary>
    /// Creates a decision that leaves the track out.
    /// </summary>
    /// <param name="reason">The reason to log.</param>
    /// <returns>The decision.</returns>
    public static OriginalVersionLanguageDecision Skip(string reason) => new(null, reason);
}

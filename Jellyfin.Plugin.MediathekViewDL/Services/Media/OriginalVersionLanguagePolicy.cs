using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Decides which language an "Originalversion" audio track is tagged with when the broadcaster's own
/// answer is missing.
/// </summary>
/// <remarks>
/// Some broadcasters mark a track as the original version without ever naming its language: ARD's
/// ONE/WDR items report the audio language as the literal string "ov", and nothing else in their
/// response says which language that is. There is nothing left to look up in that case, so the
/// subscription's own setting decides: tag the track with a configured fallback language, store it
/// as "und", or leave it out entirely.
/// </remarks>
public static class OriginalVersionLanguagePolicy
{
    /// <summary>
    /// The ISO 639-2 code for "undetermined", used when a track's language is genuinely unknown.
    /// </summary>
    public const string UndefinedLanguageCode = LanguageCodes.Undetermined;

    /// <summary>
    /// The reason logged when an original-version track is left out because nothing names its language.
    /// </summary>
    public const string SkippedMessage =
        "Die Sprache der Originalversion konnte nicht bestimmt werden und die Einstellung verlangt, "
        + "solche Tonspuren nicht zu speichern.";

    /// <summary>
    /// Determines the language code for an original-version audio track.
    /// </summary>
    /// <param name="resolvedLanguage">The language the broadcaster's own API reported, if any.</param>
    /// <param name="fallbackLanguage">The language configured for this case, if any.</param>
    /// <param name="handling">What to do when neither names a language.</param>
    /// <returns>The decision: a language code to tag with, or a skip carrying the reason.</returns>
    public static OriginalVersionLanguageDecision Decide(
        string? resolvedLanguage,
        string? fallbackLanguage,
        UndefinedOriginalVersionHandling handling)
    {
        var resolved = LanguageCodes.Normalize(resolvedLanguage);
        if (resolved is not null)
        {
            return OriginalVersionLanguageDecision.Tag(resolved);
        }

        var fallback = LanguageCodes.Normalize(fallbackLanguage);
        if (handling == UndefinedOriginalVersionHandling.UseFallbackLanguage && fallback is not null)
        {
            return OriginalVersionLanguageDecision.Tag(fallback);
        }

        // A fallback that was asked for but never filled in behaves like "store as undetermined":
        // the user wanted the track, they just never said in which language, and silently dropping
        // it would be a harsher outcome than the one they picked.
        return handling == UndefinedOriginalVersionHandling.SkipTrack
            ? OriginalVersionLanguageDecision.Skip(SkippedMessage)
            : OriginalVersionLanguageDecision.Tag(UndefinedLanguageCode);
    }

    /// <summary>
    /// Determines whether a language already attached to an item counts as "undetermined", i.e. the
    /// title parser recognized an original-version marker but no lookup or setting filled it in.
    /// </summary>
    /// <param name="languageCode">The language code to test.</param>
    /// <returns>True when the code names no language.</returns>
    public static bool IsUndefined(string? languageCode) => LanguageCodes.IsUndetermined(languageCode);
}

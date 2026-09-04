using System;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Decides which language an "Originalversion" audio track is tagged with, and whether it may be
/// downloaded at all when no language can be determined.
/// </summary>
/// <remarks>
/// Some broadcasters mark a track as the original version without ever naming its language: ARD's
/// ONE/WDR items report the audio language as the literal string "ov", and nothing else in their
/// response says which language that is. There is nothing to look up in that case, so the only
/// remaining sources are the user's own settings - and if those are empty too, the track can either
/// be tagged with the "und" (undetermined) placeholder or refused outright, depending on
/// <see cref="Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings.MetadataSettings.AllowUndefinedOriginalVersion"/>.
/// </remarks>
public static class OriginalVersionLanguagePolicy
{
    /// <summary>
    /// The ISO 639-2 code for "undetermined", used when a track's language is genuinely unknown.
    /// </summary>
    public const string UndefinedLanguageCode = "und";

    /// <summary>
    /// The message shown when an original-version track is refused because nothing names its language.
    /// </summary>
    public const string UndefinedRefusedMessage =
        "Die Sprache der Originalversion konnte nicht bestimmt werden. Der Sender nennt sie nicht, "
        + "und es ist keine Originalsprache eingestellt. Trage unter Metadaten eine Originalsprache ein "
        + "oder aktiviere \"Undefinierte Originalversionen erlauben\".";

    /// <summary>
    /// Determines the language code for an original-version audio track.
    /// </summary>
    /// <param name="resolvedLanguage">The language the broadcaster's own API reported, if any.</param>
    /// <param name="configuredLanguage">The language configured for the subscription, else the global default.</param>
    /// <param name="allowUndefined">Whether tagging the track as "und" is acceptable.</param>
    /// <returns>The decision: a language code to tag with, or a refusal carrying the reason.</returns>
    public static OriginalVersionLanguageDecision Decide(string? resolvedLanguage, string? configuredLanguage, bool allowUndefined)
    {
        if (IsUsable(resolvedLanguage))
        {
            return OriginalVersionLanguageDecision.Tag(resolvedLanguage!.Trim());
        }

        if (IsUsable(configuredLanguage))
        {
            return OriginalVersionLanguageDecision.Tag(configuredLanguage!.Trim());
        }

        return allowUndefined
            ? OriginalVersionLanguageDecision.Tag(UndefinedLanguageCode)
            : OriginalVersionLanguageDecision.Refuse(UndefinedRefusedMessage);
    }

    /// <summary>
    /// Determines whether a language already attached to an item counts as "undetermined", i.e. the
    /// title parser recognized an original-version marker but no lookup or setting filled it in.
    /// </summary>
    /// <param name="languageCode">The language code to test.</param>
    /// <returns>True when the code is empty or the "und" placeholder.</returns>
    public static bool IsUndefined(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode)
        || languageCode.Trim().Equals(UndefinedLanguageCode, StringComparison.OrdinalIgnoreCase);

    private static bool IsUsable(string? languageCode) => !IsUndefined(languageCode);
}

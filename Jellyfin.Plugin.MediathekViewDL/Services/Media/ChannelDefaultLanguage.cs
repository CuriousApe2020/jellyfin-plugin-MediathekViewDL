using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Some broadcasters are indexed by MediathekViewWeb under several channel variants that carry the
/// same content dubbed into a different default language - arte is crawled once per language as
/// "ARTE.DE", "ARTE.FR", "ARTE.EN", "ARTE.ES", "ARTE.IT" and "ARTE.PL". The crawler only adds a
/// title marker like "(Audiodeskription)" or "(Originalversion mit Untertitel)" for tracks *other*
/// than a channel's own default, so the default track itself carries no marker at all and the
/// plugin's title-based language detection cannot infer it. This per-channel override supplies it;
/// every other broadcaster publishes a single, German-default variant, so the fallback stays "deu".
/// </summary>
public static class ChannelDefaultLanguage
{
    // Verified against the MediathekView crawler (mediathekview/MServer): the six ARTE_* channel
    // names live in de/mediathekview/mlib/Const.java, and FilmeSuchen.java registers a crawler for
    // every one of them (ArteCrawler plus ArteCrawler_FR/_EN/_ES/_PL/_IT), so all six really do
    // reach the index. The codes are .NET's ThreeLetterISOLanguageName values (ISO 639-2/T, i.e.
    // "deu"/"fra" rather than "ger"/"fre"), matching what LanguageDetectionService produces.
    //
    // ARTE.DE is listed although "deu" is also the fallback: this table is the one place that says
    // what each arte variant speaks, and a complete table is easier to check against the crawler
    // than one with a silent hole in it.
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARTE.DE"] = "deu",
        ["ARTE.FR"] = "fra",
        ["ARTE.EN"] = "eng",
        ["ARTE.ES"] = "spa",
        ["ARTE.IT"] = "ita",
        ["ARTE.PL"] = "pol",
    };

    /// <summary>
    /// Gets the default language code to assume for an item from the given channel when its title
    /// carries no explicit language marker, falling back to "deu" for any channel not known to
    /// default to something else.
    /// </summary>
    /// <param name="channel">The item's channel name (e.g. "ARD", "ZDF", "ARTE.DE", "ARTE.FR"), or null/empty if unknown.</param>
    /// <returns>The 3-letter ISO default language code.</returns>
    public static string GetDefault(string? channel)
    {
        if (!string.IsNullOrWhiteSpace(channel) && Overrides.TryGetValue(channel, out var languageCode))
        {
            return languageCode;
        }

        return "deu";
    }
}

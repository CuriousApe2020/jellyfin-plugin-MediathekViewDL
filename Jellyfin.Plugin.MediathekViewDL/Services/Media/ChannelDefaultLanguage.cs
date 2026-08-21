using System.Collections.Generic;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Some broadcasters are indexed by MediathekViewWeb under multiple channel variants that share
/// the same content but dub it into a different default language - most notably arte, which is
/// crawled once as "ARTE.DE" (German default, no title marker) and once as "ARTE.FR" (French
/// default, also no title marker - the crawler only adds a marker like "(Audiodeskription)" or
/// "(Originalversion mit Untertitel)" for tracks *other* than that channel's own default). Since
/// there is no title marker to detect for the default track itself, the plugin's language
/// detection can't infer it from the title alone and needs this per-channel override; every other
/// broadcaster only publishes a single, German-default variant, so the fallback stays "deu".
/// </summary>
public static class ChannelDefaultLanguage
{
    // Confirmed via a real MediathekViewWeb query (see PR history): the same film appears under
    // both "ARTE.DE" (default track untitled, code "VA-*") and "ARTE.FR" (default track untitled,
    // code "VF-*"). Other language-specific arte channel variants may exist (the crawler also has
    // EN/ES/IT/PL variants in its source), but haven't been confirmed to actually appear in the
    // live index, so aren't included here to avoid guessing at an unverified channel name.
    private static readonly Dictionary<string, string> Overrides = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["ARTE.FR"] = "fra",
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

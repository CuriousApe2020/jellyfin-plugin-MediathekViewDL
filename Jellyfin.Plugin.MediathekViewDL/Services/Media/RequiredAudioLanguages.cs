using System.Collections.Generic;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// The languages an item must offer for a subscription to download it at all - the search-side
/// filter, as opposed to <see cref="AudioLanguageSelection"/>, which decides which of an item's
/// tracks are then actually stored.
/// </summary>
public sealed class RequiredAudioLanguages
{
    private readonly HashSet<string> _required;

    private RequiredAudioLanguages(HashSet<string> required)
    {
        _required = required;
    }

    /// <summary>
    /// Gets a value indicating whether no filter is configured, so every item qualifies.
    /// </summary>
    public bool AcceptsAnything => _required.Count == 0;

    /// <summary>
    /// Builds the filter from a subscription's accessibility settings.
    /// </summary>
    /// <param name="settings">The subscription's accessibility settings.</param>
    /// <returns>The parsed filter.</returns>
    public static RequiredAudioLanguages From(AccessibilitySettings settings)
    {
        return new RequiredAudioLanguages(LanguageCodes.ParseList(settings?.RequiredAudioLanguage));
    }

    /// <summary>
    /// Determines whether a track in the given language satisfies the filter. Any one of the
    /// configured languages is enough.
    /// </summary>
    /// <param name="languageCode">The track's language code.</param>
    /// <returns>True when the filter is satisfied.</returns>
    public bool IsSatisfiedBy(string? languageCode)
    {
        if (AcceptsAnything)
        {
            return true;
        }

        var normalized = LanguageCodes.Normalize(languageCode);
        return normalized is not null && _required.Contains(normalized);
    }
}

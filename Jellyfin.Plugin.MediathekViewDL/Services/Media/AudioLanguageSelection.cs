using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// The set of languages a subscription stores, parsed once from its settings: either everything that
/// is found, or the comma-separated list the user configured.
/// </summary>
public sealed class AudioLanguageSelection
{
    private readonly HashSet<string>? _allowed;

    private AudioLanguageSelection(HashSet<string>? allowed)
    {
        _allowed = allowed;
    }

    /// <summary>
    /// Gets a selection that keeps every language.
    /// </summary>
    public static AudioLanguageSelection Everything { get; } = new(null);

    /// <summary>
    /// Gets a value indicating whether every language is kept.
    /// </summary>
    public bool KeepsEverything => _allowed is null;

    /// <summary>
    /// Gets a value indicating whether a filter is configured but lists no usable language - the
    /// user picked "only these languages" and left the field empty or filled it with nonsense.
    /// Nothing matches such a list, so callers warn instead of silently downloading nothing.
    /// </summary>
    public bool IsEmptyFilter => _allowed is { Count: 0 };

    /// <summary>
    /// Builds the selection for the given download settings.
    /// </summary>
    /// <param name="settings">The subscription's download settings.</param>
    /// <returns>The parsed selection.</returns>
    public static AudioLanguageSelection From(BaseDownloadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.AudioLanguageMode == AudioLanguageMode.Selected
            ? new AudioLanguageSelection(LanguageCodes.ParseList(settings.SelectedAudioLanguages))
            : Everything;
    }

    /// <summary>
    /// Determines whether a track in the given language is kept. An undetermined language is never
    /// matched by a filter - what happens to such a track is decided by
    /// <see cref="UndefinedOriginalVersionHandling"/>, not here.
    /// </summary>
    /// <param name="languageCode">The track's language code.</param>
    /// <returns>True when the track is kept.</returns>
    public bool Allows(string? languageCode)
    {
        if (_allowed is null)
        {
            return true;
        }

        var normalized = LanguageCodes.Normalize(languageCode);
        return normalized is not null && _allowed.Contains(normalized);
    }
}

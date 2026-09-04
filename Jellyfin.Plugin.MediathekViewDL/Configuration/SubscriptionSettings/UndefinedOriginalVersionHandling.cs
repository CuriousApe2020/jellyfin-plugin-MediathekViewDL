namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

/// <summary>
/// What to do with an original-version track whose spoken language nothing names - neither the
/// broadcaster's own API nor a marker in the title. ARD's ONE/WDR items are the standing example:
/// they report the audio language as the literal string "ov".
/// </summary>
public enum UndefinedOriginalVersionHandling
{
    /// <summary>
    /// Store the track and tag it with the configured fallback language
    /// (<see cref="MetadataSettings.OriginalLanguage"/>).
    /// </summary>
    UseFallbackLanguage = 0,

    /// <summary>
    /// Store the track tagged as "und" (undetermined). The default, and how the plugin behaved
    /// before this setting existed. Kept even when a language filter is active - choosing this over
    /// <see cref="SkipTrack"/> is what says "I want this track, name unknown".
    /// </summary>
    StoreAsUndetermined = 1,

    /// <summary>
    /// Do not store the track at all. Only meaningful together with
    /// <see cref="AudioLanguageMode.Selected"/>: with no language to check against the list, the
    /// track cannot be shown to belong to it.
    /// </summary>
    SkipTrack = 2,
}

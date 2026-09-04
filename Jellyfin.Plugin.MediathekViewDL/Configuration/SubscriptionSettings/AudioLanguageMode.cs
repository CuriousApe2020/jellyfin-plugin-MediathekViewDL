namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

/// <summary>
/// Decides which language versions of an item are stored at all.
/// </summary>
public enum AudioLanguageMode
{
    /// <summary>
    /// Store every language version that is found - the main track plus any additional track the
    /// detection settings turn up. The default, and how the plugin behaved before this setting
    /// existed.
    /// </summary>
    All = 0,

    /// <summary>
    /// Store only tracks whose language is listed in
    /// <see cref="BaseDownloadSettings.SelectedAudioLanguages"/>. Applies to the main track too: if
    /// its language is not listed, the item's matching version is downloaded as the video instead,
    /// and an item with no matching track at all is skipped.
    /// </summary>
    Selected = 1,
}

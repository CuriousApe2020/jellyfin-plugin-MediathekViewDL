namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// How a secondary audio track was found - the two ways have their own settings.
/// </summary>
public enum SecondaryAudioDetectionSource
{
    /// <summary>
    /// Derived from the main video's own URL by substituting a broadcaster token (ARD).
    /// </summary>
    UrlDerived,

    /// <summary>
    /// Merged in from a sibling search result for the same episode (arte, ZDF/ZDFneo/3sat).
    /// </summary>
    CrossResult,
}

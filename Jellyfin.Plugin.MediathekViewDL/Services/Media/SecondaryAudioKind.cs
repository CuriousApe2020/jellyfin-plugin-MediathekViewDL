namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// The kind of secondary audio track detected via a URL-token substitution.
/// </summary>
public enum SecondaryAudioKind
{
    /// <summary>
    /// A different-language original version (e.g. English original audio for a dubbed film).
    /// </summary>
    OriginalVersion,

    /// <summary>
    /// Narrated audio description for visually impaired viewers (same language as the main track).
    /// </summary>
    AudioDescription,

    /// <summary>
    /// Speech-optimized ("klare Sprache") audio, same language as the main track.
    /// </summary>
    ClearSpeech,
}

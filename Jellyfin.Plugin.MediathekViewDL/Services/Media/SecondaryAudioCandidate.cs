namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// A single detected secondary-audio candidate for a given main video URL.
/// </summary>
/// <param name="Kind">Which kind of secondary track this is.</param>
/// <param name="Url">The derived URL for this variant.</param>
/// <param name="LanguageCode">
/// The language code to tag this track with. For <see cref="SecondaryAudioKind.OriginalVersion"/> this is a
/// best-guess default ("und") that callers should prefer to replace with a real lookup (e.g. ARD's
/// ovLanguageCode) where available. For the other kinds the track is always the same language as the main
/// (German) track.
/// </param>
public sealed record SecondaryAudioCandidate(SecondaryAudioKind Kind, string Url, string LanguageCode);

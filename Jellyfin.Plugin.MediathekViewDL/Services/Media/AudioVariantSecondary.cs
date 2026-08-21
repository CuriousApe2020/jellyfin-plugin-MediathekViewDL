using Jellyfin.Plugin.MediathekViewDL.Api.Models;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// A single sibling search-result row grouped into an <see cref="AudioVariantGroup"/> as a secondary
/// audio track.
/// </summary>
/// <param name="Item">The sibling search-result row.</param>
/// <param name="VideoInfo">The parsed <see cref="VideoInfo"/> for <paramref name="Item"/> (its
/// <see cref="Media.VideoInfo.Language"/> is used as the track's language tag).</param>
/// <param name="Kind">Which kind of secondary track this is, inferred from how it differs from the
/// group's main track.</param>
public sealed record AudioVariantSecondary(ResultItemDto Item, VideoInfo VideoInfo, SecondaryAudioKind Kind);

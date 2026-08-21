using System.Collections.Generic;
using Jellyfin.Plugin.MediathekViewDL.Api.Models;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// One episode's worth of search-result rows: the row chosen as the main (video) track, plus zero or
/// more sibling rows that <see cref="AudioVariantGroupingService"/> determined represent the same
/// episode with a different audio track (foreign-language original version, audio description, or
/// "klare Sprache"). See <see cref="AudioVariantGroupingService"/> for how rows are matched.
/// </summary>
/// <param name="MainItem">The search-result row chosen to provide the main video/audio file.</param>
/// <param name="MainVideoInfo">The parsed <see cref="VideoInfo"/> for <paramref name="MainItem"/>.</param>
/// <param name="Secondaries">Sibling rows to add as standalone secondary-audio files, if any.</param>
public sealed record AudioVariantGroup(ResultItemDto MainItem, VideoInfo MainVideoInfo, IReadOnlyList<AudioVariantSecondary> Secondaries);

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

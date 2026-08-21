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

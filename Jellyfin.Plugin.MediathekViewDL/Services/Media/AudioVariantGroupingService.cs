using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MediathekViewDL.Api.Models;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Groups a subscription's eligible search-result rows so that rows representing the same episode in
/// a different audio track become one <see cref="AudioVariantGroup"/> instead of separate, colliding
/// downloads. Complements <see cref="SecondaryAudioUrlHelper"/>: that class derives variants from a
/// single URL's own tokens (ARD, whose alternate tracks MediathekViewWeb doesn't index as separate
/// rows at all); this class instead groups rows that MediathekViewWeb already returns as fully
/// separate search results - confirmed necessary for arte (crawled once per channel variant, e.g.
/// "ARTE.DE"/"ARTE.FR", each with its own untitled default track, plus separate rows for markers like
/// "(Originalversion)"/"(Audiodeskription)") and for ZDF/ZDFneo/3sat (crawled once per language, with
/// the language named directly in the title, e.g. "(Englisch)").
/// </summary>
public static class AudioVariantGroupingService
{
    /// <summary>
    /// Channel names that are known to carry the very same episode as fully separate search-result
    /// rows because of how they're crawled (see class summary), collapsed to a shared family key so
    /// rows can be matched across them. Any channel not listed here only matches against itself -
    /// harmless for broadcasters with no known cross-channel splitting, and still useful for the
    /// same-channel, different-marker splitting arte itself also does (e.g. a plain "ARTE.DE" row
    /// next to an "ARTE.DE" row marked "(Audiodeskription)").
    /// </summary>
    private static readonly Dictionary<string, string> ChannelFamilyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARTE.DE"] = "ARTE",
        ["ARTE.FR"] = "ARTE",
        ["ZDF"] = "ZDF-FAMILIE",
        ["ZDFNEO"] = "ZDF-FAMILIE",
        ["3SAT"] = "ZDF-FAMILIE",
    };

    /// <summary>
    /// Groups the given eligible items into episode groups. Items that don't match any other item are
    /// returned as their own single-item group (no secondaries).
    /// </summary>
    /// <param name="eligibleItems">The subscription's eligible items, each with its parsed <see cref="VideoInfo"/>.</param>
    /// <returns>The resulting groups, in the same relative order as the first-seen item in each group.</returns>
    public static IReadOnlyList<AudioVariantGroup> GroupByEpisode(IReadOnlyList<(ResultItemDto Item, VideoInfo VideoInfo)> eligibleItems)
    {
        var groups = new List<AudioVariantGroup>();
        var consumed = new bool[eligibleItems.Count];

        for (var i = 0; i < eligibleItems.Count; i++)
        {
            if (consumed[i])
            {
                continue;
            }

            consumed[i] = true;
            var cluster = new List<(ResultItemDto Item, VideoInfo VideoInfo)> { eligibleItems[i] };

            for (var j = i + 1; j < eligibleItems.Count; j++)
            {
                if (consumed[j])
                {
                    continue;
                }

                if (IsSameEpisode(eligibleItems[i], eligibleItems[j]))
                {
                    consumed[j] = true;
                    cluster.Add(eligibleItems[j]);
                }
            }

            groups.AddRange(BuildGroups(cluster));
        }

        return groups;
    }

    private static IReadOnlyList<AudioVariantGroup> BuildGroups(List<(ResultItemDto Item, VideoInfo VideoInfo)> cluster)
    {
        if (cluster.Count == 1)
        {
            return new[] { new AudioVariantGroup(cluster[0].Item, cluster[0].VideoInfo, Array.Empty<AudioVariantSecondary>()) };
        }

        // Prefer the German, no-extra-track row as the main (video) track; this matches what a user
        // would expect the "primary" file to be, and what every other download path in the plugin
        // already assumes when it's the only row available.
        var main = cluster
            .OrderByDescending(x => IsPreferredMain(x.VideoInfo))
            .First();

        var secondaries = new List<AudioVariantSecondary>();

        // Rows that clustered with the main (same episode by topic/title/season/duration/timestamp)
        // but turned out not to be a usable secondary track (see below) fall back to their own
        // single-item group instead of being silently dropped - they're still real, independently
        // downloadable search results.
        var leftovers = new List<AudioVariantGroup>();

        foreach (var candidate in cluster)
        {
            if (ReferenceEquals(candidate.Item, main.Item))
            {
                continue;
            }

            // Sign language is a video difference, not an audio one - it can't be expressed as a
            // standalone audio sidecar, so leave it out of the group entirely. It's still handled as
            // its own independent item elsewhere (subject to the existing AllowSignLanguage setting).
            if (candidate.VideoInfo.HasSignLanguage || main.VideoInfo.HasSignLanguage)
            {
                leftovers.Add(new AudioVariantGroup(candidate.Item, candidate.VideoInfo, Array.Empty<AudioVariantSecondary>()));
                continue;
            }

            if (!TryGetSecondaryKind(main.VideoInfo, candidate.VideoInfo, out var kind))
            {
                // No audible difference from the main track (same language, same AD/clear-speech
                // flags) - most likely a genuine rerun on a sibling channel, not a distinct audio
                // option.
                leftovers.Add(new AudioVariantGroup(candidate.Item, candidate.VideoInfo, Array.Empty<AudioVariantSecondary>()));
                continue;
            }

            secondaries.Add(new AudioVariantSecondary(candidate.Item, candidate.VideoInfo, kind));
        }

        var result = new List<AudioVariantGroup> { new(main.Item, main.VideoInfo, secondaries) };
        result.AddRange(leftovers);
        return result;
    }

    private static bool IsPreferredMain(VideoInfo info) =>
        string.Equals(info.Language, "deu", StringComparison.OrdinalIgnoreCase)
        && !info.HasAudiodescription
        && !info.HasSignLanguage
        && !info.HasClearLanguage;

    /// <summary>
    /// Determines whether the candidate row is audibly distinguishable from the main row, and if so,
    /// which kind of secondary track it represents.
    /// </summary>
    private static bool TryGetSecondaryKind(VideoInfo main, VideoInfo candidate, out SecondaryAudioKind kind)
    {
        if (candidate.HasAudiodescription && !main.HasAudiodescription)
        {
            kind = SecondaryAudioKind.AudioDescription;
            return true;
        }

        if (candidate.HasClearLanguage && !main.HasClearLanguage)
        {
            kind = SecondaryAudioKind.ClearSpeech;
            return true;
        }

        if (!string.Equals(candidate.Language, main.Language, StringComparison.OrdinalIgnoreCase)
            && candidate.HasAudiodescription == main.HasAudiodescription
            && candidate.HasClearLanguage == main.HasClearLanguage)
        {
            kind = SecondaryAudioKind.OriginalVersion;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool IsSameEpisode((ResultItemDto Item, VideoInfo VideoInfo) a, (ResultItemDto Item, VideoInfo VideoInfo) b)
    {
        if (!string.Equals(GetChannelFamily(a.Item.Channel), GetChannelFamily(b.Item.Channel), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals((a.Item.Topic ?? string.Empty).Trim(), (b.Item.Topic ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(a.VideoInfo.Title.Trim(), b.VideoInfo.Title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (a.VideoInfo.SeasonNumber != b.VideoInfo.SeasonNumber || a.VideoInfo.EpisodeNumber != b.VideoInfo.EpisodeNumber)
        {
            return false;
        }

        if (a.VideoInfo.AbsoluteEpisodeNumber != b.VideoInfo.AbsoluteEpisodeNumber)
        {
            return false;
        }

        // Guard against unrelated, coincidentally-same-titled items: require duration and broadcast
        // time to be close. Audio-description tracks can run noticeably longer due to narration, so
        // allow generous tolerance (60s or 10%, whichever is bigger); a sibling channel's/language's
        // crawl entry for the same episode can lag the main entry by up to roughly two days in
        // practice (confirmed via real MediathekViewWeb data for arte and ZDF).
        var durationTolerance = TimeSpan.FromSeconds(Math.Max(60, 0.1 * Math.Max(a.Item.Duration.TotalSeconds, b.Item.Duration.TotalSeconds)));
        if ((a.Item.Duration - b.Item.Duration).Duration() > durationTolerance)
        {
            return false;
        }

        var timeDelta = (a.Item.Timestamp - b.Item.Timestamp).Duration();
        return timeDelta <= TimeSpan.FromHours(48);
    }

    private static string GetChannelFamily(string? channel) =>
        !string.IsNullOrWhiteSpace(channel) && ChannelFamilyAliases.TryGetValue(channel, out var family)
            ? family
            : channel ?? string.Empty;
}

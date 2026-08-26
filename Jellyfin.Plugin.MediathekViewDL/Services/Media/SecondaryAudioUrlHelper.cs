using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.SubscriptionSettings;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Detects when a single ARD-style video URL contains one or more language/audio-variant tokens
/// (e.g. "_sendeton_" for German, "_originalversion_" for the original-language track,
/// "_audiodeskription_" for audio description, "_klaresprache_" for speech-optimized audio)
/// and derives the alternate URLs by substitution, without needing a separate search-result item.
/// </summary>
public static class SecondaryAudioUrlHelper
{
    private const string MainToken = "_sendeton_";

    private static readonly (string Token, SecondaryAudioKind Kind, string LanguageCode)[] KnownVariants =
    {
        ("_originalversion_", SecondaryAudioKind.OriginalVersion, "und"),
        ("_audiodeskription_", SecondaryAudioKind.AudioDescription, "deu"),
        ("_klaresprache_", SecondaryAudioKind.ClearSpeech, "deu"),
    };

    /// <summary>
    /// Detects every known secondary-audio variant derivable from the given main video URL.
    /// </summary>
    /// <param name="mainVideoUrl">The resolved main (typically German) video URL.</param>
    /// <returns>Zero or more detected candidates, in the order defined by the known variants.</returns>
    public static IReadOnlyList<SecondaryAudioCandidate> DetectCandidates(string? mainVideoUrl)
    {
        var results = new List<SecondaryAudioCandidate>();

        if (string.IsNullOrWhiteSpace(mainVideoUrl) || !mainVideoUrl.Contains(MainToken, StringComparison.OrdinalIgnoreCase))
        {
            return results;
        }

        foreach (var (token, kind, lang) in KnownVariants)
        {
            var candidateUrl = mainVideoUrl.Replace(MainToken, token, StringComparison.OrdinalIgnoreCase);
            results.Add(new SecondaryAudioCandidate(kind, candidateUrl, lang));
        }

        return results;
    }

    /// <summary>
    /// Determines whether the given secondary-audio kind is enabled for download in the given
    /// download settings. Shared by both the subscription and manual-download code paths so the
    /// two can't silently drift apart.
    /// </summary>
    /// <param name="settings">The download settings to check (subscription-level or global default).</param>
    /// <param name="kind">The secondary audio kind to check.</param>
    /// <returns>True if the given kind is enabled for download.</returns>
    public static bool IsKindEnabled(BaseDownloadSettings settings, SecondaryAudioKind kind)
    {
        return kind switch
        {
            SecondaryAudioKind.OriginalVersion => settings.DownloadOriginalVersionAudio,
            SecondaryAudioKind.AudioDescription => settings.DownloadAudioDescriptionAudio,
            SecondaryAudioKind.ClearSpeech => settings.DownloadClearSpeechAudio,
            _ => false,
        };
    }
}

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
    /// Determines whether a secondary track of the given kind, found the given way, is collected at
    /// all. Language versions and accessibility tracks are governed by separate switches - a
    /// subscription can collect one without the other - so both settings groups are needed here.
    /// Shared by the subscription and manual-download paths so the two can't silently drift apart.
    /// </summary>
    /// <param name="download">The download settings (subscription-level or global default).</param>
    /// <param name="accessibility">The accessibility settings (subscription-level or global default).</param>
    /// <param name="kind">The secondary audio kind to check.</param>
    /// <param name="source">How the track was found.</param>
    /// <returns>True if such a track is collected.</returns>
    /// <remarks>
    /// For <see cref="SecondaryAudioKind.OriginalVersion"/> this only says the track is *collected*;
    /// whether it is ultimately kept is decided afterwards by the subscription's language selection
    /// (see <see cref="AudioLanguageSelection"/>), which needs the resolved language to judge.
    /// </remarks>
    public static bool IsKindEnabled(
        BaseDownloadSettings download,
        AccessibilitySettings accessibility,
        SecondaryAudioKind kind,
        SecondaryAudioDetectionSource source)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(accessibility);

        var urlDerived = source == SecondaryAudioDetectionSource.UrlDerived;

        // Each kind carries its own pair of detection switches, so a subscription can look for
        // audio description without looking for "klare Sprache", and either without looking for
        // foreign-language versions.
        var detectionEnabled = kind switch
        {
            SecondaryAudioKind.OriginalVersion => urlDerived
                ? download.DetectUndetectedSecondaryAudio
                : download.DetectCrossResultAudioVariants,
            SecondaryAudioKind.AudioDescription => urlDerived
                ? accessibility.DetectUndetectedAudioDescription
                : accessibility.DetectCrossResultAudioDescription,
            SecondaryAudioKind.ClearSpeech => urlDerived
                ? accessibility.DetectUndetectedClearSpeech
                : accessibility.DetectCrossResultClearSpeech,
            _ => false,
        };

        if (!detectionEnabled)
        {
            return false;
        }

        return kind switch
        {
            SecondaryAudioKind.OriginalVersion => true,
            SecondaryAudioKind.AudioDescription => accessibility.AllowAudioDescription,
            SecondaryAudioKind.ClearSpeech => accessibility.DownloadClearSpeech,
            _ => false,
        };
    }

    /// <summary>
    /// Gets a value indicating whether any kind of track is looked for across sibling search
    /// results. Grouping the result stream is only worth its buffering cost when at least one kind
    /// would actually be picked up.
    /// </summary>
    /// <param name="download">The subscription's download settings.</param>
    /// <param name="accessibility">The subscription's accessibility settings.</param>
    /// <returns>True when at least one kind is collected from sibling results.</returns>
    public static bool AnyCrossResultDetectionEnabled(
        BaseDownloadSettings download,
        AccessibilitySettings accessibility)
        => IsKindEnabled(download, accessibility, SecondaryAudioKind.OriginalVersion, SecondaryAudioDetectionSource.CrossResult)
            || IsKindEnabled(download, accessibility, SecondaryAudioKind.AudioDescription, SecondaryAudioDetectionSource.CrossResult)
            || IsKindEnabled(download, accessibility, SecondaryAudioKind.ClearSpeech, SecondaryAudioDetectionSource.CrossResult);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Resolves the "Originalversion" language for ardmediathek.de items via ARD's own page-gateway
/// API, which knows the real spoken language the public search API never surfaces: primarily from
/// the audio tracks listed in the item's media collection (see
/// <see cref="FindForeignAudioLanguageCode"/>), and from an "ovLanguageCode" field where the
/// response happens to carry one.
/// </summary>
public class ArdOriginalVersionLanguageResolver : IBroadcasterOriginalVersionLanguageResolver
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ArdOriginalVersionLanguageResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArdOriginalVersionLanguageResolver"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="logger">The logger.</param>
    public ArdOriginalVersionLanguageResolver(HttpClient httpClient, ILogger<ArdOriginalVersionLanguageResolver> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool CanResolve(string itemWebsiteUrl)
    {
        return itemWebsiteUrl.Contains("ardmediathek.de", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<string?> TryGetOriginalVersionLanguageAsync(string itemWebsiteUrl, CancellationToken cancellationToken)
    {
        // Take the last non-empty path segment as the item's crid. This works for both
        // MediathekViewWeb's own short-form URL (ardmediathek.de/video/{crid}, no slug or
        // publisher segments) and the full "pretty" browser URL
        // (ardmediathek.de/video/{slug}/{slug}/{publisher}/{crid}) - the crid is always last.
        var pathPart = itemWebsiteUrl.Split('?')[0].TrimEnd('/');
        var segments = pathPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var itemId = segments.Length > 0 ? segments[^1] : null;

        if (string.IsNullOrWhiteSpace(itemId) || itemId.Length < 20)
        {
            _logger.LogWarning("Could not extract ARD item id from URL '{Url}'.", itemWebsiteUrl);
            return null;
        }

        // "mcV6=true" is what makes the page-gateway include the item's media collection at all -
        // without it the response carries only page/teaser metadata and every lookup came back
        // empty, no matter which language the item actually is (same parameter yt-dlp's ARD
        // extractor relies on).
        var apiUrl = $"https://api.ardmediathek.de/page-gateway/pages/ard/item/{itemId}?devicetype=pc&embedded=false&mcV6=true";
        _logger.LogInformation("Looking up original-version language for '{Url}' via '{ApiUrl}'.", itemWebsiteUrl, apiUrl);

        try
        {
            using var response = await _httpClient.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ARD page-gateway lookup for '{Url}' failed with status {Status}.", itemWebsiteUrl, response.StatusCode);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            // A present-but-useless "ovLanguageCode" (ARD's own "und" placeholder) must not end the
            // search either - it would leave the track exactly as untagged as no lookup at all.
            var languageCode = NormalizeLanguageCode(FindOvLanguageCode(doc.RootElement))
                ?? FindForeignAudioLanguageCode(doc.RootElement);
            _logger.LogInformation("Original-version language lookup for '{Url}' resolved to '{Language}'.", itemWebsiteUrl, languageCode ?? "(not found)");
            return languageCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve original-version language for '{Url}'.", itemWebsiteUrl);
            return null;
        }
    }

    /// <summary>
    /// Recursively searches the JSON document for a non-empty "ovLanguageCode" property,
    /// since its exact nesting depth in ARD's response isn't stable across content types.
    /// </summary>
    private static string? FindOvLanguageCode(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("ovLanguageCode")
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return property.Value.GetString();
                }

                var nested = FindOvLanguageCode(property.Value);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindOvLanguageCode(item);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Fallback for items whose response carries no "ovLanguageCode": ARD lists the item's audio
    /// tracks as { "kind": "standard" | "audio-description" | "original-version", "languageCode":
    /// "deu" } objects inside an "audios" array, so the original version's real language can be read
    /// from there instead. A track ARD itself marks as the original version wins; otherwise the
    /// first track in a language other than German is taken, which is what an "Originalversion"
    /// track is by definition.
    /// </summary>
    /// <param name="root">The parsed page-gateway response.</param>
    /// <returns>The 3-letter ISO language code, or null when no usable audio track was listed.</returns>
    private static string? FindForeignAudioLanguageCode(JsonElement root)
    {
        var audioTracks = new List<(string? Kind, string Language)>();
        CollectAudioTracks(root, audioTracks);

        foreach (var (kind, language) in audioTracks)
        {
            if (kind is null
                || (!kind.Contains("original", StringComparison.OrdinalIgnoreCase)
                    && !kind.Equals("ov", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var markedLanguage = NormalizeLanguageCode(language);
            if (markedLanguage is not null)
            {
                return markedLanguage;
            }
        }

        foreach (var (_, language) in audioTracks)
        {
            var normalized = NormalizeLanguageCode(language);
            if (normalized is not null && !IsGerman(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    /// <summary>
    /// Recursively collects every audio-track entry (an object with a "languageCode", inside an
    /// "audios" array) from the response. Restricting this to "audios" arrays keeps subtitle and
    /// teaser language fields out of the result.
    /// </summary>
    private static void CollectAudioTracks(JsonElement element, List<(string? Kind, string Language)> audioTracks)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("audios") && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var audio in property.Value.EnumerateArray())
                    {
                        if (audio.ValueKind != JsonValueKind.Object
                            || !audio.TryGetProperty("languageCode", out var languageProperty)
                            || languageProperty.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var language = languageProperty.GetString();
                        if (string.IsNullOrWhiteSpace(language))
                        {
                            continue;
                        }

                        var kind = audio.TryGetProperty("kind", out var kindProperty) && kindProperty.ValueKind == JsonValueKind.String
                            ? kindProperty.GetString()
                            : null;

                        audioTracks.Add((kind, language));
                    }

                    continue;
                }

                CollectAudioTracks(property.Value, audioTracks);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectAudioTracks(item, audioTracks);
            }
        }
    }

    /// <summary>
    /// Brings ARD's language codes into the plugin's usual 3-letter form. ARD normally uses
    /// 3-letter codes ("deu", "eng") but 2-letter and locale forms ("en", "en-GB") show up too;
    /// the "und" placeholder is treated as "no language", since returning it would be no better
    /// than not resolving at all.
    /// </summary>
    private static string? NormalizeLanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var primary = language.Trim().Split('-')[0];

        if (primary.Length == 3 && !primary.Equals("und", StringComparison.OrdinalIgnoreCase))
        {
            return primary.ToLowerInvariant();
        }

        if (primary.Length == 2)
        {
            try
            {
                var threeLetterCode = new CultureInfo(primary).ThreeLetterISOLanguageName;
                return string.IsNullOrWhiteSpace(threeLetterCode) || threeLetterCode.Equals("und", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : threeLetterCode;
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsGerman(string threeLetterCode) =>
        threeLetterCode.Equals("deu", StringComparison.OrdinalIgnoreCase)
        || threeLetterCode.Equals("ger", StringComparison.OrdinalIgnoreCase);
}

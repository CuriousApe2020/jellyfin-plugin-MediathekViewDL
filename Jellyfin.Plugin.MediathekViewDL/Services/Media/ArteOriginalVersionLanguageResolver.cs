using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Resolves the "Originalversion" language for arte.tv items via arte's own player-config API.
/// Unlike ARD, arte's crawler already surfaces original-version tracks as separate search
/// results (title-suffixed "(Originalversion)"/"(Originalversion mit Untertitel)"), but without
/// naming which language that original version actually is - this resolver fills that in.
/// </summary>
public class ArteOriginalVersionLanguageResolver : IBroadcasterOriginalVersionLanguageResolver
{
    // arte.tv video IDs look like "109067-000-A" (six digits, dash, three digits, dash, A or F).
    private static readonly Regex VideoIdRegex = new(@"(?<id>\d{6}-\d{3}-[AF])", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ILogger<ArteOriginalVersionLanguageResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArteOriginalVersionLanguageResolver"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="logger">The logger.</param>
    public ArteOriginalVersionLanguageResolver(HttpClient httpClient, ILogger<ArteOriginalVersionLanguageResolver> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool CanResolve(string itemWebsiteUrl)
    {
        return itemWebsiteUrl.Contains("arte.tv", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<string?> TryGetOriginalVersionLanguageAsync(string itemWebsiteUrl, CancellationToken cancellationToken)
    {
        var match = VideoIdRegex.Match(itemWebsiteUrl);
        if (!match.Success)
        {
            _logger.LogWarning("Could not extract arte video id from URL '{Url}'.", itemWebsiteUrl);
            return null;
        }

        var videoId = match.Groups["id"].Value;
        var apiUrl = $"https://api.arte.tv/api/player/v2/config/de/{videoId}";
        _logger.LogInformation("Looking up original-version language for '{Url}' via '{ApiUrl}'.", itemWebsiteUrl, apiUrl);

        try
        {
            using var response = await _httpClient.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("arte player-config lookup for '{Url}' failed with status {Status}.", itemWebsiteUrl, response.StatusCode);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var languageCode = FindOriginalVersionLanguageCode(doc.RootElement);
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
    /// Recursively searches arte's player-config document (confirmed shape: an array at
    /// data.attributes.streams[].versions[], each entry like
    /// { "code": "VO-STF", "label": "Originalfassung - UT französisch", "audioLanguage": "en",
    /// "audioDescription": false }) for the stream-version object whose "code" is prefixed "VO"
    /// (arte's own marker for "Version Originale") and returns its "audioLanguage", converted to a
    /// 3-letter ISO code. Deliberately does NOT parse "label"/"shortLabel" for the language name -
    /// confirmed against a real response that the label often describes the *subtitle* language
    /// instead (e.g. "UT französisch" on an English-original track), not the audio. The exact
    /// nesting isn't assumed, since arte's API shape isn't publicly documented beyond this sample;
    /// walking the whole tree means an unrelated restructuring degrades to "not found" rather than
    /// breaking.
    /// </summary>
    private string? FindOriginalVersionLanguageCode(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetOriginalVersionAudioLanguage(element, out var languageCode))
            {
                return languageCode;
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindOriginalVersionLanguageCode(property.Value);
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
                var nested = FindOriginalVersionLanguageCode(item);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Tests whether the given JSON object is a stream-version entry for the original-version
    /// track ("code" prefixed "VO"), and if so, converts its "audioLanguage" (a 2-letter code, e.g.
    /// "en") to the plugin's usual 3-letter form (e.g. "eng").
    /// </summary>
    private static bool TryGetOriginalVersionAudioLanguage(JsonElement versionElement, out string? languageCode)
    {
        languageCode = null;

        if (!versionElement.TryGetProperty("code", out var codeProperty)
            || codeProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var code = codeProperty.GetString();
        if (code is null || !code.StartsWith("VO", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!versionElement.TryGetProperty("audioLanguage", out var audioLanguageProperty)
            || audioLanguageProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var audioLanguage = audioLanguageProperty.GetString();
        if (string.IsNullOrWhiteSpace(audioLanguage) || audioLanguage.Equals("und", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        languageCode = ToThreeLetterCode(audioLanguage);
        return languageCode is not null;
    }

    private static string? ToThreeLetterCode(string twoLetterCode)
    {
        try
        {
            return new CultureInfo(twoLetterCode).ThreeLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}

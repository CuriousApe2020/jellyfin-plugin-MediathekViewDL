using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <inheritdoc/>
public class ArdOriginalVersionLanguageResolver : IArdOriginalVersionLanguageResolver
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
    public async Task<string?> TryGetOriginalVersionLanguageAsync(string? itemWebsiteUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemWebsiteUrl))
        {
            _logger.LogWarning("Cannot look up original-version language: no website URL was provided for this item.");
            return null;
        }

        if (!itemWebsiteUrl.Contains("ardmediathek.de", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Skipping original-version language lookup for '{Url}': not an ardmediathek.de URL.", itemWebsiteUrl);
            return null;
        }

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

        var apiUrl = $"https://api.ardmediathek.de/page-gateway/pages/ard/item/{itemId}?devicetype=pc&embedded=false";
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

            var languageCode = FindOvLanguageCode(doc.RootElement);
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
}

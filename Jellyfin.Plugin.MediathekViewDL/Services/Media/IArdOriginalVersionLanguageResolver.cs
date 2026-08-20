using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Resolves the correct language code for an ARD title's "Originalversion" audio track
/// by querying ARD's own page-gateway API, instead of assuming a fixed language.
/// </summary>
public interface IArdOriginalVersionLanguageResolver
{
    /// <summary>
    /// Attempts to resolve the original-version language code (e.g. "eng", "fra") for the given
    /// ardmediathek.de item page URL.
    /// </summary>
    /// <param name="itemWebsiteUrl">The item's ardmediathek.de page URL (from the search result's UrlWebsite field).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The language code if found, otherwise null.</returns>
    Task<string?> TryGetOriginalVersionLanguageAsync(string? itemWebsiteUrl, CancellationToken cancellationToken);
}

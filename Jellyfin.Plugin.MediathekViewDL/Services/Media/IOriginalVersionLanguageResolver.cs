using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Resolves the correct language code for a title's "Originalversion" (original-language) audio
/// track by querying the relevant broadcaster's own API, instead of assuming a fixed language.
/// Dispatches to whichever broadcaster-specific resolver (see
/// <see cref="IBroadcasterOriginalVersionLanguageResolver"/>) recognizes the item's website URL.
/// </summary>
public interface IOriginalVersionLanguageResolver
{
    /// <summary>
    /// Attempts to resolve the original-version language code (e.g. "eng", "fra") for the given
    /// item page URL, trying every known broadcaster-specific resolver in turn.
    /// </summary>
    /// <param name="itemWebsiteUrl">The item's broadcaster page URL (from the search result's UrlWebsite field).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The language code if found, otherwise null.</returns>
    Task<string?> TryGetOriginalVersionLanguageAsync(string? itemWebsiteUrl, CancellationToken cancellationToken);
}

using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// A single broadcaster's strategy for resolving the real language of an "Originalversion" audio
/// track. Implementations are broadcaster-specific (different broadcasters expose this through
/// completely different APIs); <see cref="IOriginalVersionLanguageResolver"/> dispatches to
/// whichever implementation recognizes a given item URL.
/// </summary>
public interface IBroadcasterOriginalVersionLanguageResolver
{
    /// <summary>
    /// Determines whether this resolver knows how to handle the given item website URL.
    /// </summary>
    /// <param name="itemWebsiteUrl">The item's broadcaster page URL.</param>
    /// <returns>True if this resolver's <see cref="TryGetOriginalVersionLanguageAsync"/> should be tried.</returns>
    bool CanResolve(string itemWebsiteUrl);

    /// <summary>
    /// Attempts to resolve the original-version language code for the given item page URL. Only
    /// called after <see cref="CanResolve"/> returned true for the same URL.
    /// </summary>
    /// <param name="itemWebsiteUrl">The item's broadcaster page URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The language code if found, otherwise null.</returns>
    Task<string?> TryGetOriginalVersionLanguageAsync(string itemWebsiteUrl, CancellationToken cancellationToken);
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <inheritdoc/>
public class OriginalVersionLanguageResolver : IOriginalVersionLanguageResolver
{
    private readonly IEnumerable<IBroadcasterOriginalVersionLanguageResolver> _broadcasterResolvers;
    private readonly ILogger<OriginalVersionLanguageResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OriginalVersionLanguageResolver"/> class.
    /// </summary>
    /// <param name="broadcasterResolvers">Every registered broadcaster-specific resolver.</param>
    /// <param name="logger">The logger.</param>
    public OriginalVersionLanguageResolver(
        IEnumerable<IBroadcasterOriginalVersionLanguageResolver> broadcasterResolvers,
        ILogger<OriginalVersionLanguageResolver> logger)
    {
        _broadcasterResolvers = broadcasterResolvers;
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

        foreach (var resolver in _broadcasterResolvers)
        {
            if (!resolver.CanResolve(itemWebsiteUrl))
            {
                continue;
            }

            return await resolver.TryGetOriginalVersionLanguageAsync(itemWebsiteUrl, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Skipping original-version language lookup for '{Url}': no broadcaster resolver recognizes this URL.", itemWebsiteUrl);
        return null;
    }
}

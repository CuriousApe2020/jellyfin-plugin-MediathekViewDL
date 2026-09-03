using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration;
using Jellyfin.Plugin.MediathekViewDL.Data;
using Jellyfin.Plugin.MediathekViewDL.Services;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Queue;
using Jellyfin.Plugin.MediathekViewDL.Services.Library;
using Jellyfin.Plugin.MediathekViewDL.Services.Subscriptions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Tasks;

/// <summary>
/// Scheduled task to process download subscriptions.
/// </summary>
public class DownloadScheduledTask : IScheduledTask
{
    private readonly ILogger<DownloadScheduledTask> _logger;
    private readonly ISubscriptionProcessor _subscriptionProcessor;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly ILocalMediaScanner _localMediaScanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadScheduledTask"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="subscriptionProcessor">The subscription processor.</param>
    /// <param name="configurationProvider">The configuration provider.</param>
    /// <param name="localMediaScanner">The local media scanner, whose remembered scans this task resets at the start of every run.</param>
    public DownloadScheduledTask(
        ILogger<DownloadScheduledTask> logger,
        ISubscriptionProcessor subscriptionProcessor,
        IConfigurationProvider configurationProvider,
        ILocalMediaScanner localMediaScanner)
    {
        _logger = logger;
        _subscriptionProcessor = subscriptionProcessor;
        _configurationProvider = configurationProvider;
        _localMediaScanner = localMediaScanner;
    }

    /// <inheritdoc />
    public string Name => "Mediathek Abo-Downloader";

    /// <inheritdoc />
    public string Key => Constants.GetSchedTaskKey("MediathekAboDownloader");

    /// <inheritdoc />
    public string Category => "CuriousApes-MediathekView-Downloader";

    /// <inheritdoc />
    public string Description => "Sucht nach neuen Inhalten für Abonnements und fügt sie der Download-Warteschlange hinzu.";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Run every 6 hours
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(6).Ticks };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.InitializationException is not null)
        {
            _logger.LogError("Mediathek subscription download task aborted because the plugin failed to initialize: {ErrorMessage}", Plugin.Instance.InitializationException.Message);
            return;
        }

        _logger.LogInformation("Starting Mediathek subscription download task.");
        progress.Report(0);

        // Subscriptions in this run share what the scanner reads, which is what keeps one run from
        // walking the same library tree once per subscription. That sharing must not reach across
        // runs: between two runs the library can have changed in ways nothing here would notice.
        _localMediaScanner.InvalidateCache();

        var config = _configurationProvider.ConfigurationOrNull;
        if (config == null || config.Subscriptions.Count == 0)
        {
            _logger.LogInformation("No subscriptions configured. Task finished.");
            return;
        }

        var newLastRun = DateTime.UtcNow;
        // Snapshot under the lock: an admin editing a subscription from the web UI mutates this
        // very list, and a structural change landing mid-copy can throw or produce a torn result.
        var subscriptions = SubscriptionsLock.Run(() => config.Subscriptions.ToList());

        var subscriptionProgressShare = subscriptions.Count > 0 ? 100.0 / subscriptions.Count : 0;

        for (int i = 0; i < subscriptions.Count; i++)
        {
            var subscription = subscriptions[i];

            if (!subscription.IsEnabled)
            {
                _logger.LogDebug("Skipping disabled subscription '{SubscriptionName}'.", subscription.Name);
                progress.Report((double)(i + 1) * subscriptionProgressShare);
                continue;
            }

            var baseProgressForSubscription = (double)i * subscriptionProgressShare;
            progress.Report(baseProgressForSubscription);

            _logger.LogInformation("Processing subscription: {SubscriptionName}", subscription.Name);

            await _subscriptionProcessor.ProcessSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false);

            progress.Report(baseProgressForSubscription + subscriptionProgressShare);
        }

        // Save the new timestamp
        config.LastRun = newLastRun;
        _configurationProvider.TryUpdate(config);

        progress.Report(100);
        _logger.LogInformation("Mediathek subscription discovery task finished. Jobs are in the download queue.");
    }
}

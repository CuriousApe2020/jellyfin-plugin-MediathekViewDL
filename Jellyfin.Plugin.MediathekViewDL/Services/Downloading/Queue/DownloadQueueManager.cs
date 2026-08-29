using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration;
using Jellyfin.Plugin.MediathekViewDL.Data;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Queue;

/// <summary>
/// Manages the download queue and execution.
/// </summary>
public sealed class DownloadQueueManager : IDownloadQueueManager, IDisposable
{
    private readonly ConcurrentDictionary<Guid, ActiveDownload> _activeDownloads = new();
    private readonly Channel<ActiveDownload> _queueChannel;
    private readonly SemaphoreSlim _concurrencySemaphore = new(1, 1); // Limit to 1 concurrent download
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DownloadQueueManager> _logger;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ConcurrentDictionary<Guid, Task> _runningDownloads = new();
    private readonly Task _queueProcessor;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadQueueManager"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="configurationProvider">The configuration provider.</param>
    public DownloadQueueManager(
        IServiceScopeFactory scopeFactory,
        ILogger<DownloadQueueManager> logger,
        IConfigurationProvider configurationProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configurationProvider = configurationProvider;
        _queueChannel = Channel.CreateUnbounded<ActiveDownload>();
        _queueProcessor = Task.Run(ProcessQueueAsync);
    }

    /// <inheritdoc />
    public void QueueJob(DownloadJob job, Guid? subscriptionId = null)
    {
        CleanupOldDownloads();

        var activeDownload = new ActiveDownload
        {
            Job = job,
            Status = DownloadStatus.Queued,
            SubscriptionId = subscriptionId,
            // Linked to shutdown so Dispose() actually stops an in-progress download instead of
            // letting it run on against a disposed service scope.
            Cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token)
        };

        if (_activeDownloads.TryAdd(activeDownload.Id, activeDownload))
        {
            if (_queueChannel.Writer.TryWrite(activeDownload))
            {
                _logger.LogInformation("Queued download job '{Title}' (ID: {Id}).", job.Title, activeDownload.Id);
            }
            else
            {
                _logger.LogError("Failed to write download job '{Title}' (ID: {Id}) to channel.", job.Title, activeDownload.Id);
                activeDownload.Status = DownloadStatus.Failed;
                activeDownload.ErrorMessage = "Internal error: Queue full or closed.";
            }
        }
    }

    private void CleanupOldDownloads()
    {
        // Remove downloads that are finished/failed/cancelled and older than 24 hours
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var keysToRemove = _activeDownloads
            .Where(kvp => (kvp.Value.Status == DownloadStatus.Finished ||
                           kvp.Value.Status == DownloadStatus.Failed ||
                           kvp.Value.Status == DownloadStatus.Cancelled) &&
                          kvp.Value.CreatedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            if (_activeDownloads.TryRemove(key, out var removed))
            {
                removed.Cts.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void CancelJob(Guid id)
    {
        if (_activeDownloads.TryGetValue(id, out var download))
        {
            if (download.Status == DownloadStatus.Finished || download.Status == DownloadStatus.Failed || download.Status == DownloadStatus.Cancelled)
            {
                throw new InvalidOperationException($"Cannot cancel a download that is already in state '{download.Status}'.");
            }

            download.Cts.Cancel();
            download.Status = DownloadStatus.Cancelled;
            _logger.LogInformation("Cancelled download job '{Title}' (ID: {Id}).", download.Job.Title, id);
        }
        else
        {
            throw new KeyNotFoundException($"Download job with ID '{id}' not found.");
        }
    }

    /// <inheritdoc />
    public void CancelAllJobs()
    {
        _logger.LogInformation("Cancellation of all download jobs requested.");
        foreach (var download in _activeDownloads.Values)
        {
            if (download.Status == DownloadStatus.Queued ||
                download.Status == DownloadStatus.Downloading ||
                download.Status == DownloadStatus.Processing)
            {
                try
                {
                    download.Cts.Cancel();
                    download.Status = DownloadStatus.Cancelled;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cancelling job '{Title}' (ID: {Id}).", download.Job.Title, download.Id);
                }
            }
        }
    }

    /// <inheritdoc />
    public void ClearInactiveJobs()
    {
        _logger.LogInformation("Clearing all inactive download jobs from list.");
        var keysToRemove = _activeDownloads
            .Where(kvp => kvp.Value.Status == DownloadStatus.Finished ||
                           kvp.Value.Status == DownloadStatus.Failed ||
                           kvp.Value.Status == DownloadStatus.Cancelled)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            if (_activeDownloads.TryRemove(key, out var removed))
            {
                removed.Cts.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<ActiveDownload> GetActiveDownloads()
    {
        return _activeDownloads.Values.OrderByDescending(d => d.CreatedAt);
    }

    /// <summary>
    /// Disposes the manager, cancelling the queue loop and any download still running and waiting
    /// (bounded) for them to unwind before the primitives they use are torn down.
    /// </summary>
    /// <remarks>
    /// The wait matters: previously this cancelled and then disposed <c>_shutdownCts</c> and
    /// <c>_concurrencySemaphore</c> immediately, while a download - which can easily run for
    /// minutes - was still holding the semaphore. Its <c>finally</c> then released an
    /// already-disposed semaphore, throwing <see cref="ObjectDisposedException"/> on a
    /// fire-and-forget task where nothing observed it. Cancelling first also actually stops those
    /// downloads now, because their token sources are linked to <c>_shutdownCts</c>.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down - nothing left to signal.
        }

        // Give the queue loop and any in-flight download a bounded chance to observe the
        // cancellation and run their cleanup before the primitives disappear underneath them.
        try
        {
            var pending = _runningDownloads.Values.Append(_queueProcessor).ToArray();
            _ = Task.WaitAll(pending, TimeSpan.FromSeconds(15));
        }
        catch (AggregateException)
        {
            // WaitAll surfaces the cancellations we just triggered - the expected outcome here.
        }
        catch (ObjectDisposedException)
        {
            // A task's underlying state was already torn down; nothing left to wait for.
        }

        foreach (var download in _activeDownloads.Values)
        {
            download.Cts.Dispose();
        }

        _activeDownloads.Clear();
        _runningDownloads.Clear();
        _shutdownCts.Dispose();
        _concurrencySemaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (await _queueChannel.Reader.WaitToReadAsync(_shutdownCts.Token).ConfigureAwait(false))
            {
                while (_queueChannel.Reader.TryRead(out var download))
                {
                    if (download.Status == DownloadStatus.Cancelled)
                    {
                        continue;
                    }

                    await _concurrencySemaphore.WaitAsync(_shutdownCts.Token).ConfigureAwait(false);

                    if (download.Status == DownloadStatus.Cancelled)
                    {
                        _concurrencySemaphore.Release();
                        continue;
                    }

                    var downloadId = download.Id;
                    var runningTask = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await ExecuteDownloadAsync(download).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                _logger.LogInformation("Download job '{Title}' (ID: {Id}) was cancelled.", download.Job.Title, download.Id);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error executing download job '{Title}' (ID: {Id}).", download.Job.Title, download.Id);
                            }
                            finally
                            {
                                _runningDownloads.TryRemove(downloadId, out _);

                                try
                                {
                                    _concurrencySemaphore.Release();
                                }
                                catch (ObjectDisposedException)
                                {
                                    // Shutdown won the race and already tore the semaphore down;
                                    // there is no queue left for this slot to be handed to.
                                }
                            }
                        },
                        _shutdownCts.Token);

                    // A short download can reach its finally - and its TryRemove - before this
                    // line runs, which would strand the finished task in the dictionary forever.
                    // Pruning completed entries on every insert makes that race self-healing;
                    // with a concurrency limit of 1 there is at most a handful to look at.
                    foreach (var finished in _runningDownloads.Where(kvp => kvp.Value.IsCompleted).ToList())
                    {
                        _runningDownloads.TryRemove(finished.Key, out _);
                    }

                    _runningDownloads[downloadId] = runningTask;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in download queue loop.");
        }
    }

    private async Task ExecuteDownloadAsync(ActiveDownload download)
    {
        download.Status = DownloadStatus.Downloading;
        _logger.LogInformation("Starting execution of download job '{Title}' (ID: {Id}).", download.Job.Title, download.Id);

        using var scope = _scopeFactory.CreateScope();
        var downloadManager = scope.ServiceProvider.GetRequiredService<IDownloadManager>();
        var historyRepository = scope.ServiceProvider.GetRequiredService<IDownloadHistoryRepository>();
        var libraryManager = scope.ServiceProvider.GetRequiredService<ILibraryManager>();

        var progress = new Progress<double>(p =>
        {
            download.Progress = p;
            if (p > 90 && download.Status == DownloadStatus.Downloading)
            {
                download.Status = DownloadStatus.Processing;
            }
        });

        try
        {
            var result = await downloadManager.ExecuteJobAsync(download.Job, progress, download.Cts.Token).ConfigureAwait(false);

            if (result.Success)
            {
                download.Status = DownloadStatus.Finished;
                download.Progress = 100;
                download.ItemResults = result.ItemResults;

                // Save every item in the job to history. Items grouped in from their own distinct
                // search result (see AudioVariantGroupingService) carry their own SourceItemId so
                // *that* result is recorded too - otherwise it would keep reappearing as a fresh,
                // ungrouped item on every future subscription run.
                foreach (var item in download.Job.DownloadItems)
                {
                    await historyRepository.AddAsync(
                        item.SourceUrl,
                        item.SourceItemId ?? download.Job.ItemId,
                        download.SubscriptionId ?? Guid.Empty,
                        item.DestinationPath,
                        download.Job.Title,
                        download.Job.ItemInfo.Language).ConfigureAwait(false);
                }

                if (_configurationProvider.ConfigurationOrNull?.Download.ScanLibraryAfterDownload == true && _activeDownloads.Values.All(d => d.Status != DownloadStatus.Queued))
                {
                    _logger.LogInformation("Triggering library scan (all downloads finished).");
                    libraryManager.QueueLibraryScan();
                }
            }
            else if (download.Status != DownloadStatus.Cancelled)
            {
                download.Status = DownloadStatus.Failed;
                download.ErrorMessage = "Download fehlgeschlagen (Details siehe unten).";
                download.ItemResults = result.ItemResults;
            }
        }
        catch (OperationCanceledException)
        {
            download.Status = DownloadStatus.Cancelled;
        }
        catch (Exception ex)
        {
            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Exception during download job '{Title}' (ID: {Id}).", download.Job.Title, download.Id);
        }
    }
}

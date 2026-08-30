using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Handlers;
using Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Models;
using Jellyfin.Plugin.MediathekViewDL.Services.Library;
using Jellyfin.Plugin.MediathekViewDL.Services.Metadata;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Downloading;

/// <summary>
/// Service responsible for executing download jobs.
/// </summary>
public class DownloadManager : IDownloadManager
{
    private readonly ILogger<DownloadManager> _logger;
    private readonly INfoService _nfoService;
    private readonly IEnumerable<IDownloadHandler> _downloadHandlers;
    private readonly IStrmValidationService _urlValidationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadManager"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="nfoService">The NFO service.</param>
    /// <param name="downloadHandlers">The download handlers.</param>
    /// <param name="urlValidationService">The URL validation service.</param>
    public DownloadManager(
        ILogger<DownloadManager> logger,
        INfoService nfoService,
        IEnumerable<IDownloadHandler> downloadHandlers,
        IStrmValidationService urlValidationService)
    {
        _logger = logger;
        _nfoService = nfoService;
        _downloadHandlers = downloadHandlers;
        _urlValidationService = urlValidationService;
    }

    /// <summary>
    /// Executes a single download job.
    /// </summary>
    /// <param name="job">The job to execute.</param>
    /// <param name="progress">The progress reporter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the download job with per-item details.</returns>
    public async Task<DownloadJobResult> ExecuteJobAsync(DownloadJob job, IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting download job for '{Title}'.", job.Title);
        var overallSuccess = true;
        var cancelled = false;
        var itemResults = new List<DownloadItemResult>();

        foreach (var item in job.DownloadItems)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            _logger.LogInformation("Processing download item: {Type} -> {Path}", item.JobType, item.DestinationPath);
            if (File.Exists(item.DestinationPath))
            {
                _logger.LogDebug("File '{Path}' already exists. Skipping download.", item.DestinationPath);
                itemResults.Add(new DownloadItemResult
                {
                    DestinationPath = item.DestinationPath,
                    JobType = item.JobType,
                    Success = true,
                    Skipped = true
                });
                continue;
            }

            try
            {
                if (!await TryResolveValidUrlAsync(item, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogError("Invalid URL: {Url}", item.SourceUrl);
                    overallSuccess = false;
                    itemResults.Add(new DownloadItemResult
                    {
                        DestinationPath = item.DestinationPath,
                        JobType = item.JobType,
                        Success = false,
                        ErrorMessage = $"Ungültige URL: {item.SourceUrl}"
                    });
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                // A cancelled job is not a failed URL. Without this the general handler below
                // swallowed the cancellation, logged it at error level as if the broadcaster had
                // gone away, and carried on validating every remaining item of the job.
                cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "URL validation failed for {Url}", item.SourceUrl);
                overallSuccess = false;
                itemResults.Add(new DownloadItemResult
                {
                    DestinationPath = item.DestinationPath,
                    JobType = item.JobType,
                    Success = false,
                    ErrorMessage = $"URL-Validierung fehlgeschlagen: {ex.Message}"
                });
                continue;
            }

            var directory = Path.GetDirectoryName(item.DestinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create directory '{Directory}'.", directory);
                    overallSuccess = false;
                    itemResults.Add(new DownloadItemResult
                    {
                        DestinationPath = item.DestinationPath,
                        JobType = item.JobType,
                        Success = false,
                        ErrorMessage = $"Verzeichnis konnte nicht erstellt werden: {ex.Message}"
                    });
                    continue;
                }
            }

            var handler = _downloadHandlers.FirstOrDefault(h => h.CanHandle(item.JobType));
            if (handler != null)
            {
                bool itemSuccess;
                try
                {
                    itemSuccess = await handler.ExecuteAsync(item, job, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }

                overallSuccess &= itemSuccess;
                itemResults.Add(new DownloadItemResult
                {
                    DestinationPath = item.DestinationPath,
                    JobType = item.JobType,
                    Success = itemSuccess,
                    ErrorMessage = itemSuccess ? null : $"Download fehlgeschlagen ({item.JobType})"
                });
            }
            else
            {
                _logger.LogError("No handler found for download type: {Type}", item.JobType);
                overallSuccess = false;
                itemResults.Add(new DownloadItemResult
                {
                    DestinationPath = item.DestinationPath,
                    JobType = item.JobType,
                    Success = false,
                    ErrorMessage = $"Kein Handler für Typ '{item.JobType}' gefunden"
                });
            }
        }

        if (overallSuccess && !cancelled)
        {
            progress.Report(100);
        }

        // The NFO describes the media that landed, so a failed *sidecar* must not suppress it:
        // previously any failure at all (a subtitle 404 being the common one) skipped NFO creation
        // entirely, leaving a perfectly good video file without its metadata for good - the retry
        // on the next run skips the already-present video via the File.Exists check above and so
        // never reached this branch again either.
        var mediaLanded = itemResults.Any(r => r.Success && r.JobType != DownloadType.SubtitleDownload);
        if (mediaLanded && job.NfoMetadata is not null && !File.Exists(job.NfoMetadata.FilePath))
        {
            _nfoService.CreateNfo(job.NfoMetadata);
        }

        // Surfaced only after the NFO step above: a job cancelled midway can still have a complete
        // video file on disk, and that file gets its metadata now or never - the next run skips it
        // via the File.Exists shortcut and never reaches this code again.
        if (cancelled)
        {
            overallSuccess = false;
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new DownloadJobResult
        {
            Success = overallSuccess,
            ItemResults = itemResults
        };
    }

    /// <summary>
    /// Validates <paramref name="item"/>'s <see cref="DownloadItem.SourceUrl"/>, falling back through
    /// <see cref="DownloadItem.FallbackSourceUrls"/> (in order) if it fails. A job can sit in the
    /// download queue - currently strictly one download at a time - for a while after its URL was
    /// already resolved and validated at discovery time; broadcaster CDN URLs are often
    /// time-limited, so the previously-valid URL can have expired by the time execution actually
    /// starts even though a lower-quality sibling from the same search result is still reachable.
    /// On success, mutates <paramref name="item"/>.SourceUrl in place to whichever URL actually
    /// validated, since every <see cref="IDownloadHandler"/> reads it directly.
    /// </summary>
    /// <param name="item">The download item to resolve a working URL for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if <paramref name="item"/>.SourceUrl now points at a validated URL; false if none of it and its fallbacks validated.</returns>
    private async Task<bool> TryResolveValidUrlAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        var deadUrl = item.SourceUrl;
        if (await ValidateCandidateAsync(deadUrl, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (item.FallbackSourceUrls is not { Count: > 0 })
        {
            return false;
        }

        foreach (var fallbackUrl in item.FallbackSourceUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(fallbackUrl) || fallbackUrl == deadUrl)
            {
                continue;
            }

            if (await ValidateCandidateAsync(fallbackUrl, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogWarning(
                    "URL for '{Path}' expired while queued ('{DeadUrl}' - originally valid at discovery time). Falling back to '{FallbackUrl}'.",
                    item.DestinationPath,
                    deadUrl,
                    fallbackUrl);
                item.SourceUrl = fallbackUrl;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Validates a single URL, treating a validation-time exception (e.g. a transient network
    /// error) the same as an outright invalid URL - just move on to the next candidate - rather
    /// than aborting the whole fallback attempt. A genuine cancellation still propagates: only
    /// non-cancellation exceptions are swallowed here.
    /// </summary>
    private async Task<bool> ValidateCandidateAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _urlValidationService.ValidateUrlAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "URL candidate validation threw for '{Url}'; treating as invalid.", url);
            return false;
        }
    }
}

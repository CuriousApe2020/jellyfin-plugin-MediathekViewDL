using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Models;

/// <summary>
/// Represents an active or recently active download in the queue.
/// </summary>
public class ActiveDownload
{
    /// <summary>
    /// Gets or sets the unique identifier for this download instance.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the subscription ID if this download belongs to one.
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// Gets or sets the download job details.
    /// </summary>
    public DownloadJob Job { get; set; } = null!;

    /// <summary>
    /// Gets or sets the current status.
    /// </summary>
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;

    /// <summary>
    /// Gets or sets the progress (0-100).
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets the error message if the job failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the per-item results after execution.
    /// </summary>
    public IReadOnlyList<DownloadItemResult>? ItemResults { get; set; }

    /// <summary>
    /// Gets or sets the time when the download was created/queued.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the cancellation token source for this job. The queue manager replaces the
    /// default with one linked to its shutdown token, so that stopping the plugin/server also
    /// cancels a download that is already running instead of leaving it to write into a
    /// half-torn-down service scope.
    /// </summary>
    [JsonIgnore]
    public CancellationTokenSource Cts { get; set; } = new();
}

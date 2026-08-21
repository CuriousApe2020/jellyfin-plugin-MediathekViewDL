namespace Jellyfin.Plugin.MediathekViewDL.Api.Models
{
    /// <summary>
    /// Advanced download options. Used for manual downloads.
    /// </summary>
    public class AdvancedDownloadOptions
    {
        /// <summary>
        /// Gets the item to download.
        /// </summary>
        public required ResultItemDto Item { get; init; }

        /// <summary>
        /// Gets the download path.
        /// </summary>
        public required string DownloadPath { get; init; }

        /// <summary>
        /// Gets the file name.
        /// </summary>
        public required string FileName { get; init; }

        /// <summary>
        /// Gets a value indicating whether to download subtitles.
        /// </summary>
        public bool DownloadSubtitles { get; init; }

        /// <summary>
        /// Gets the Name of the Subtitle file.
        /// </summary>
        public string SubtitleName { get; init; } = string.Empty;

        /// <summary>
        /// Gets the URL of a secondary audio-only or video stream to download as an additional standalone
        /// audio track next to the main video. Optional. Obtain this URL with e.g. "yt-dlp -F" on the
        /// broadcaster's page.
        /// </summary>
        public string? SecondaryAudioUrl { get; init; }

        /// <summary>
        /// Gets the language code (e.g. "eng", "fra") for the secondary audio track.
        /// </summary>
        public string? SecondaryAudioLanguage { get; init; }
    }
}

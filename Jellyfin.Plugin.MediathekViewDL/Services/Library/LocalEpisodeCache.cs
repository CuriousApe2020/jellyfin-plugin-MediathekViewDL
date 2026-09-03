using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Library;

/// <summary>
/// Cache for local episodes to enable fast duplicate detection.
/// </summary>
public class LocalEpisodeCache
{
    private readonly Dictionary<(int Season, int Episode, string Language), string> _seasonEpisodes = new();
    private readonly Dictionary<(int Absolute, string Language), string> _absoluteEpisodes = new();

    // Language-agnostic view of the same episodes: which languages an episode already has on disk,
    // and which file new audio tracks would be attached to. The per-language dictionaries above
    // cannot answer that - they are keyed *by* language, so they can only say "this exact language
    // is present", never "this episode exists, but in a different language".
    private readonly Dictionary<(int Season, int Episode), EpisodeVariants> _seasonEpisodeVariants = new();
    private readonly Dictionary<int, EpisodeVariants> _absoluteEpisodeVariants = new();

    // Every scanned media file, by full path. The dictionaries above only ever see files whose
    // name carries an episode number, which leaves films and other unnumbered items out entirely -
    // in one real library 945 of 952 scanned files under the film root reached none of them. This
    // set is what lets a caller ask "did the scan see the exact file this download would write?".
    //
    // Unlike the dictionaries this keeps growing after the scan: a job that has been built claims
    // the paths it is going to write, so the rest of the run treats them as taken. That is why it
    // is the one part of this class that needs a lock - the scan itself finishes before anyone
    // reads the instance, but claims arrive while other subscriptions are already reading it.
    private readonly HashSet<string> _filePaths = new(StringComparer.OrdinalIgnoreCase);

    // Files this run has decided to write but has not written yet. Kept apart from the scanned
    // ones on purpose: both mean "do not download this again", but only a file that is actually
    // there may be recorded in the download history. A claim is a plan, and plans fail - writing
    // it to history would tell every later run that a file exists which nobody ever wrote.
    private readonly HashSet<string> _claimedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _filePathsLock = new();

    /// <summary>
    /// Gets the count of unique Season/Episode pairs in the cache.
    /// </summary>
    public int SeasonEpisodeCount => _seasonEpisodes.Count;

    /// <summary>
    /// Gets the count of unique Absolute Episode numbers in the cache.
    /// </summary>
    public int AbsoluteEpisodeCount => _absoluteEpisodes.Count;

    /// <summary>
    /// Gets the count of media files seen by the scan, numbered or not.
    /// </summary>
    public int FileCount
    {
        get
        {
            lock (_filePathsLock)
            {
                return _filePaths.Count;
            }
        }
    }

    /// <summary>
    /// Records a media file the scan found, independently of whether its name yields an episode
    /// number.
    /// </summary>
    /// <param name="filePath">The full path to the file.</param>
    public void AddFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var normalized = NormalizePath(filePath);
        lock (_filePathsLock)
        {
            _filePaths.Add(normalized);
        }
    }

    /// <summary>
    /// Marks a file this run intends to write, so nothing else in the run targets it again.
    /// </summary>
    /// <param name="filePath">The full path that will be written.</param>
    public void ClaimFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var normalized = NormalizePath(filePath);
        lock (_filePathsLock)
        {
            _claimedPaths.Add(normalized);
        }
    }

    /// <summary>
    /// Checks whether the scan found a file at exactly this path.
    /// </summary>
    /// <param name="filePath">The full path to look for.</param>
    /// <returns>True if the scan saw that file.</returns>
    public bool ContainsFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalized = NormalizePath(filePath);
        lock (_filePathsLock)
        {
            return _filePaths.Contains(normalized);
        }
    }

    /// <summary>
    /// Checks whether something else in this run has already decided to write this path.
    /// </summary>
    /// <param name="filePath">The full path to look for.</param>
    /// <returns>True if the path is spoken for.</returns>
    public bool IsClaimed(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalized = NormalizePath(filePath);
        lock (_filePathsLock)
        {
            return _claimedPaths.Contains(normalized);
        }
    }

    /// <summary>
    /// Adds an episode to the cache.
    /// </summary>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <param name="absolute">The absolute episode number.</param>
    /// <param name="filePath">The full path to the file.</param>
    /// <param name="language">The language code (default "deu").</param>
    /// <param name="isSidecarAudio">
    /// True if <paramref name="filePath"/> is a secondary-audio track sitting next to an episode's
    /// video (a ".mka") rather than the episode's own video file. Such a file still proves its
    /// language is present, but is not itself something further tracks can be attached to.
    /// </param>
    public void Add(int? season, int? episode, int? absolute, string filePath, string language = "deu", bool isSidecarAudio = false)
    {
        var lang = language.ToLowerInvariant();
        if (season.HasValue && episode.HasValue)
        {
            _seasonEpisodes[(season.Value, episode.Value, lang)] = filePath;
            AddVariant(_seasonEpisodeVariants, (season.Value, episode.Value), filePath, lang, isSidecarAudio);
        }

        if (absolute.HasValue)
        {
            _absoluteEpisodes[(absolute.Value, lang)] = filePath;
            AddVariant(_absoluteEpisodeVariants, absolute.Value, filePath, lang, isSidecarAudio);
        }
    }

    /// <summary>
    /// Finds an existing local video for the episode described by <paramref name="videoInfo"/>
    /// regardless of language, so a newly found audio variant can be attached to it instead of
    /// being downloaded as a second, near-duplicate video.
    /// </summary>
    /// <param name="videoInfo">The video info identifying the episode.</param>
    /// <param name="videoFilePath">The existing video file new tracks should sit next to.</param>
    /// <param name="existingLanguages">Every language already present for this episode, main file and sidecars alike.</param>
    /// <returns>True if a local video for this episode exists.</returns>
    public bool TryGetEpisodeVideo(
        VideoInfo? videoInfo,
        [NotNullWhen(true)] out string? videoFilePath,
        out IReadOnlyCollection<string> existingLanguages)
    {
        videoFilePath = null;
        existingLanguages = Array.Empty<string>();

        if (videoInfo == null)
        {
            return false;
        }

        EpisodeVariants? variants = null;
        if (videoInfo.SeasonNumber.HasValue && videoInfo.EpisodeNumber.HasValue)
        {
            _seasonEpisodeVariants.TryGetValue((videoInfo.SeasonNumber.Value, videoInfo.EpisodeNumber.Value), out variants);
        }

        if (variants == null && videoInfo.AbsoluteEpisodeNumber.HasValue)
        {
            _absoluteEpisodeVariants.TryGetValue(videoInfo.AbsoluteEpisodeNumber.Value, out variants);
        }

        if (variants?.VideoFilePath == null)
        {
            return false;
        }

        videoFilePath = variants.VideoFilePath;
        existingLanguages = variants.Languages;
        return true;
    }

    /// <summary>
    /// Checks if the cache contains the episode described in the VideoInfo object.
    /// </summary>
    /// <param name="videoInfo">The video info object to check.</param>
    /// <returns>True if the episode exists in the cache, otherwise false.</returns>
    public bool Contains(VideoInfo videoInfo)
    {
        if (videoInfo == null)
        {
            return false;
        }

        return Contains(videoInfo.SeasonNumber, videoInfo.EpisodeNumber, videoInfo.AbsoluteEpisodeNumber, videoInfo.Language);
    }

    /// <summary>
    /// Checks if the cache contains the specified episode.
    /// </summary>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <param name="absolute">The absolute episode number.</param>
    /// <param name="language">The language code (default "deu").</param>
    /// <returns>True if the episode exists in the cache, otherwise false.</returns>
    public bool Contains(int? season, int? episode, int? absolute, string language = "deu")
    {
        var lang = language.ToLowerInvariant();
        if (season.HasValue && episode.HasValue && _seasonEpisodes.ContainsKey((season.Value, episode.Value, lang)))
        {
            return true;
        }

        if (absolute.HasValue && _absoluteEpisodes.ContainsKey((absolute.Value, lang)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the file path for an existing episode if found in the cache.
    /// </summary>
    /// <param name="videoInfo">The video info object to search for.</param>
    /// <returns>The full file path if found, otherwise null.</returns>
    public string? GetExistingFilePath(VideoInfo videoInfo)
    {
        if (videoInfo == null)
        {
            return null;
        }

        var lang = videoInfo.Language.ToLowerInvariant();

        if (videoInfo.SeasonNumber.HasValue && videoInfo.EpisodeNumber.HasValue)
        {
            if (_seasonEpisodes.TryGetValue((videoInfo.SeasonNumber.Value, videoInfo.EpisodeNumber.Value, lang), out var path))
            {
                return path;
            }
        }

        if (videoInfo.AbsoluteEpisodeNumber.HasValue)
        {
            if (_absoluteEpisodes.TryGetValue((videoInfo.AbsoluteEpisodeNumber.Value, lang), out var path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Puts a path into one comparable form. The two sides that meet here reach it differently -
    /// one from enumerating a directory, the other from composing a name - so a stray separator or
    /// a relative segment would otherwise make two spellings of the same file look like two files.
    /// </summary>
    /// <param name="filePath">The path to normalize.</param>
    /// <returns>The normalized path, or the input unchanged if it cannot be normalized.</returns>
    private static string NormalizePath(string filePath)
    {
        try
        {
            return Path.GetFullPath(filePath);
        }
        catch (ArgumentException)
        {
            // Malformed enough that Path cannot make sense of it. Comparing it verbatim is no
            // worse than dropping it, and a duplicate check is not the place to fail a run.
            return filePath;
        }
        catch (NotSupportedException)
        {
            return filePath;
        }
        catch (PathTooLongException)
        {
            return filePath;
        }
    }

    private static void AddVariant<TKey>(Dictionary<TKey, EpisodeVariants> target, TKey key, string filePath, string language, bool isSidecarAudio)
        where TKey : notnull
    {
        if (!target.TryGetValue(key, out var variants))
        {
            variants = new EpisodeVariants();
            target[key] = variants;
        }

        variants.Languages.Add(language);

        // First video wins: with several videos for one episode (e.g. a leftover from an older
        // naming scheme) any choice is arbitrary, and picking a stable one at least keeps repeated
        // runs consistent instead of attaching tracks to a different file each time.
        if (!isSidecarAudio && variants.VideoFilePath == null)
        {
            variants.VideoFilePath = filePath;
        }
    }

    /// <summary>
    /// The languages an episode already has on disk, plus the video file further audio tracks would
    /// be written next to.
    /// </summary>
    private sealed class EpisodeVariants
    {
        public string? VideoFilePath { get; set; }

        public HashSet<string> Languages { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

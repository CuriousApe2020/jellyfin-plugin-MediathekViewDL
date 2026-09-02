using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// Gets the count of unique Season/Episode pairs in the cache.
    /// </summary>
    public int SeasonEpisodeCount => _seasonEpisodes.Count;

    /// <summary>
    /// Gets the count of unique Absolute Episode numbers in the cache.
    /// </summary>
    public int AbsoluteEpisodeCount => _absoluteEpisodes.Count;

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

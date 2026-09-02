using System;
using System.IO;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Downloading.Helpers;

/// <summary>
/// Checks whether the plugin may actually write into a target directory.
/// </summary>
internal static class TargetDirectoryAccess
{
    /// <summary>
    /// Probes <paramref name="directory"/> by creating and removing an empty file.
    /// </summary>
    /// <remarks>
    /// There is no portable way to ask .NET "may this process write here": permission bits, ACLs,
    /// ownership and read-only mounts all have a say, and with container bind mounts a
    /// non-writable library directory is a routine misconfiguration rather than an exotic one.
    /// Actually attempting a write is the only answer that matches what the download does a moment
    /// later, and asking up front costs a few milliseconds instead of a failed transfer - ffmpeg
    /// reports a non-writable output as a plain non-zero exit code, indistinguishable from the
    /// broadcaster having gone away. The reason is passed back rather than swallowed so the caller
    /// can say what is actually wrong: a full disk fails here too, and calling that a permission
    /// problem would send the user looking in the wrong place.
    /// </remarks>
    /// <param name="directory">The directory to probe. Expected to exist.</param>
    /// <returns>Null if the directory is writable, otherwise the reason it is not.</returns>
    public static string? GetWriteFailure(string directory)
    {
        var probePath = Path.Combine(directory, $".mvdl-write-probe-{Guid.NewGuid():N}");
        try
        {
            using (var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                probe.Flush();
            }

            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            return ex.Message;
        }
        catch (IOException ex)
        {
            // A read-only mount and a full disk both surface here rather than as
            // UnauthorizedAccessException.
            return ex.Message;
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch (UnauthorizedAccessException)
            {
                // Nothing useful left to do: the probe file is empty, and the caller is about to
                // report the directory as unusable anyway.
            }
            catch (IOException)
            {
                // Same as above.
            }
        }
    }
}

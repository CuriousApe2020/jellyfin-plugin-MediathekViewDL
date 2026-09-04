using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.MediathekViewDL.Services.Media;

/// <summary>
/// Normalizes the language codes the plugin deals with into one shape: the three-letter ISO 639-2/T
/// form the rest of the plugin uses ("deu", "eng"), or nothing at all when the value names no
/// language.
/// </summary>
public static class LanguageCodes
{
    /// <summary>
    /// The ISO 639-2 code for "undetermined" - a real, valid tag, but one that names no language.
    /// </summary>
    public const string Undetermined = "und";

    /// <summary>
    /// Values that appear where a language is expected but name none: ARD's own "ov" marker for
    /// "this is the original version" plus the ISO placeholders for undetermined, uncoded and
    /// multiple languages.
    /// </summary>
    private static readonly HashSet<string> NonLanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ov", Undetermined, "mis", "mul", "zxx",
    };

    /// <summary>
    /// Brings a language code into the plugin's three-letter form. Accepts two-letter and locale
    /// forms ("en", "en-GB") as well; anything that names no language returns null.
    /// </summary>
    /// <param name="languageCode">The code to normalize.</param>
    /// <returns>The three-letter code, or null.</returns>
    public static string? Normalize(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        var primary = languageCode.Trim().Split('-')[0];
        if (NonLanguageCodes.Contains(primary))
        {
            return null;
        }

        if (primary.Length == 3)
        {
            return primary.ToLowerInvariant();
        }

        if (primary.Length == 2)
        {
            try
            {
                var threeLetterCode = new CultureInfo(primary).ThreeLetterISOLanguageName;
                return string.IsNullOrWhiteSpace(threeLetterCode) || NonLanguageCodes.Contains(threeLetterCode)
                    ? null
                    : threeLetterCode;
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a user-typed list of ISO codes into normalized three-letter codes, dropping anything
    /// that names no language. Commas are the documented separator; semicolons and whitespace are
    /// accepted too, because a list typed by hand rarely uses only commas.
    /// </summary>
    /// <param name="configured">The configured list, e.g. "deu, eng".</param>
    /// <returns>The normalized codes; empty when nothing usable was listed.</returns>
    public static HashSet<string> ParseList(string? configured)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return codes;
        }

        foreach (var part in configured.Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = Normalize(part);
            if (normalized is not null)
            {
                codes.Add(normalized);
            }
        }

        return codes;
    }

    /// <summary>
    /// Determines whether a code names no language - empty, the "und" placeholder, or one of the
    /// other markers that stand in for a missing language.
    /// </summary>
    /// <param name="languageCode">The code to test.</param>
    /// <returns>True when the code names no language.</returns>
    public static bool IsUndetermined(string? languageCode) => Normalize(languageCode) is null;
}

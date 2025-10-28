// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any non-commercial
// purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AgentPrimer.Utilities;

/// <summary>
/// Detects language-specific resource files to infer supported UI languages.
/// </summary>
internal static class LocalizationAnalyzer
{
    private static readonly string[] ResourceExtensions =
    [
        ".resx",
        ".resw"
    ];

    public static string[] GetSupportedUiLanguages(IReadOnlyCollection<string> trackedFiles)
    {
        if (trackedFiles.Count == 0)
            return [];

        var regionSpecific = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var neutralByLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var trackedFile in trackedFiles.Where(IsResourceFile))
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(trackedFile);
            if (string.IsNullOrEmpty(fileNameWithoutExtension))
                continue;

            var lastDot = fileNameWithoutExtension.LastIndexOf('.');
            if (lastDot < 0 || lastDot == fileNameWithoutExtension.Length - 1)
                continue;

            var culturePart = fileNameWithoutExtension[(lastDot + 1)..].Replace('_', '-');
            if (string.IsNullOrWhiteSpace(culturePart))
                continue;

            try
            {
                var culture = CultureInfo.GetCultureInfo(culturePart);
                var displayName = GetDisplayName(culture);
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    if (culture.IsNeutralCulture)
                    {
                        var languageKey = culture.TwoLetterISOLanguageName;
                        if (!string.IsNullOrEmpty(languageKey) && !neutralByLanguage.ContainsKey(languageKey))
                            neutralByLanguage[languageKey] = displayName;
                    }
                    else
                    {
                        if (!regionSpecific.ContainsKey(culture.Name))
                            regionSpecific[culture.Name] = displayName;
                    }
                }
            }
            catch (CultureNotFoundException)
            {
                // Ignore custom resource names that don't map to a known culture.
            }
        }

        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in regionSpecific.Values.OrderBy(o => o, StringComparer.OrdinalIgnoreCase))
            languages.Add(name);

        foreach (var entry in neutralByLanguage.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!HasRegionSpecificCulture(entry.Key, regionSpecific.Keys))
                languages.Add(entry.Value);
        }

        return languages.OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsResourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Length > 0 &&
               ResourceExtensions.Any(resExtension =>
                   extension.Equals(resExtension, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetDisplayName(CultureInfo culture)
    {
        if (!culture.IsNeutralCulture && culture.Name.Contains('-'))
        {
            try
            {
                var languageName = culture.EnglishName.Split('(')[0].Trim();
                var region = new RegionInfo(culture.Name);
                return $"{languageName} ({region.TwoLetterISORegionName})";
            }
            catch (ArgumentException)
            {
                // If RegionInfo can't be created, fall back to the English name.
            }
        }

        return culture.EnglishName;
    }

    private static bool HasRegionSpecificCulture(string languageKey, IEnumerable<string> cultureNames)
    {
        foreach (var name in cultureNames)
        {
            if (name.StartsWith(languageKey + "-", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

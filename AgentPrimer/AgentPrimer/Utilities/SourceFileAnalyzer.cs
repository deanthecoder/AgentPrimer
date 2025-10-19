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

namespace AgentPrimer.Utilities;

/// <summary>
/// Calculates language statistics from the list of tracked source files.
/// </summary>
internal static class SourceFileAnalyzer
{
    public static Dictionary<string, double> AnalyzeFileTypes(IReadOnlyCollection<string> sourceFiles)
    {
        var fileTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "C#",
            [".fs"] = "F#",
            [".vb"] = "VB.NET",
            [".c"] = "C/C++",
            [".h"] = "C/C++",
            [".hpp"] = "C/C++",
            [".cpp"] = "C/C++",
            [".cc"] = "C/C++",
            [".mm"] = "Objective-C",
            [".m"] = "Objective-C",
            [".java"] = "Java",
            [".kt"] = "Kotlin",
            [".swift"] = "Swift",
            [".js"] = "JavaScript",
            [".jsx"] = "JavaScript",
            [".ts"] = "TypeScript",
            [".tsx"] = "TypeScript",
            [".py"] = "Python",
            [".rb"] = "Ruby",
            [".php"] = "PHP",
            [".go"] = "Go",
            [".rs"] = "Rust",
            [".dart"] = "Dart",
            [".sql"] = "SQL",
            [".ps1"] = "PowerShell",
            [".sh"] = "Shell",
            [".bat"] = "Batch",
            [".yml"] = "YAML",
            [".yaml"] = "YAML"
        };

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var extensions =
            sourceFiles
                .Select(o => Path.GetExtension(o.ToLower()))
                .Where(o => fileTypeMap.ContainsKey(o))
                .ToArray();

        if (extensions.Length == 0)
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var extension in extensions)
        {
            var language = fileTypeMap[extension];
            counts[language] = counts.TryGetValue(language, out var count)
                ? count + 1
                : 1;
        }

        var total = (double)extensions.Length;
        return counts
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value / total, StringComparer.OrdinalIgnoreCase);
    }
}

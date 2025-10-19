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

using System.Text;

namespace AgentPrimer.Utilities;

/// <summary>
/// Attempts to determine the preferred English variant used within a repository by counting spelling differences.
/// </summary>
internal static class EnglishPreferenceAnalyzer
{
    private const long MaxFileSizeBytes = 100_000;

    private static readonly SpellingPair[] SpellingPairs =
    [
        new("color", "colour"),
        new("favorite", "favourite"),
        new("honor", "honour"),
        new("behavior", "behaviour"),
        new("organize", "organise"),
        new("organization", "organisation"),
        new("analyze", "analyse"),
        new("analyzer", "analyser"),
        new("recognize", "recognise"),
        new("realize", "realise"),
        new("center", "centre"),
        new("meter", "metre"),
        new("theater", "theatre"),
        new("defense", "defence"),
        new("license", "licence"),
        new("apologize", "apologise"),
        new("utilize", "utilise"),
        new("catalog", "catalogue"),
        new("dialog", "dialogue"),
        new("gray", "grey"),
        new("traveling", "travelling"),
        new("canceled", "cancelled"),
        new("modeling", "modelling"),
        new("labeled", "labelled")
    ];

    internal enum EnglishVariant
    {
        American,
        British
    }

    public static EnglishVariant DeterminePreferredEnglishVariant(string repoPath, IReadOnlyCollection<string> trackedFiles)
    {
        var americanCount = 0;
        var britishCount = 0;

        foreach (var trackedFile in trackedFiles)
        {
            if (!SourceFileAnalyzer.IsRecognizedSourceExtension(Path.GetExtension(trackedFile)))
                continue;

            var fullPath = Path.Combine(repoPath, trackedFile);

            if (!TryReadText(fullPath, out var contents))
                continue;

            foreach (var pair in SpellingPairs)
            {
                americanCount += CountOccurrences(contents, pair.American);
                britishCount += CountOccurrences(contents, pair.British);
            }
        }

        return britishCount > americanCount ? EnglishVariant.British : EnglishVariant.American;
    }

    private static bool TryReadText(string path, out string text)
    {
        text = string.Empty;

        try
        {
            if (!File.Exists(path))
                return false;

            if (ShouldSkip(path))
                return false;

            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = reader.ReadToEnd();
            if (ContainsBinaryData(content))
                return false;

            text = content;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Failed to read '{path}': {ex.Message}");
            return false;
        }
    }

    private static bool ShouldSkip(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
            return true;

        return fileInfo.Length > MaxFileSizeBytes;

    }

    private static bool ContainsBinaryData(string content) =>
        content.Any(ch => ch == '\0');

    private static int CountOccurrences(string text, string word)
    {
        var count = 0;
        var position = 0;

        while (position < text.Length)
        {
            var index = text.IndexOf(word, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;

            if (IsWordBoundary(text, index, word.Length))
                count++;

            position = index + word.Length;
        }

        return count;
    }

    private static bool IsWordBoundary(string text, int index, int length)
    {
        if (index > 0 && char.IsLetter(text[index - 1]))
            return false;

        var endIndex = index + length;
        return endIndex >= text.Length || !char.IsLetter(text[endIndex]);
    }

    private readonly record struct SpellingPair(string American, string British);
}

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
/// Safely enumerates files beneath the repository root while swallowing common IO issues.
/// </summary>
internal static class RepositoryFileEnumerator
{
    public static string[] EnumerateFilesSafe(string rootDirectory, string searchPattern)
    {
        try
        {
            return Directory.EnumerateFiles(rootDirectory, searchPattern, SearchOption.AllDirectories).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Failed to enumerate '{searchPattern}' files: {ex.Message}");
            return [];
        }
    }
}

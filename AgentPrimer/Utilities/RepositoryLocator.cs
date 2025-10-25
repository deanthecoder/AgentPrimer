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

using System.ComponentModel;

namespace AgentPrimer.Utilities;

/// <summary>
/// Provides utilities for locating the nearest git repository root from a starting directory.
/// </summary>
internal static class RepositoryLocator
{
    public static bool TryFindGitRoot(string startPath, out string repositoryPath)
    {
        repositoryPath = startPath;

        if (string.IsNullOrWhiteSpace(startPath))
            return false;

        var currentPath = startPath;

        while (!IsGitRepository(currentPath))
        {
            var parent = Directory.GetParent(currentPath)?.FullName;
            if (parent is null)
                return false;

            currentPath = parent;
        }

        repositoryPath = currentPath;
        return true;
    }

    private static bool IsGitRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var gitDirectory = Path.Combine(path, ".git");
            return Directory.Exists(gitDirectory) || File.Exists(gitDirectory);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}

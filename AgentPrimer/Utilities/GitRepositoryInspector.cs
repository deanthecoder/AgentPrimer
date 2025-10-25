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

using System.Diagnostics;
using System.Text.Json;
using DTC.Core.Extensions;

namespace AgentPrimer.Utilities;

/// <summary>
/// Aggregates git-related inspection helpers such as listing tracked files and resolving GitHub metadata.
/// </summary>
internal static class GitRepositoryInspector
{
    public static (string Url, string Description)[] GetRepositories(string repoPath)
    {
        var slugs = new List<string>();

        foreach (var remoteUrl in GetRepoRemoteUrls(repoPath))
        {
            if (TryGetRepoSlug(remoteUrl, out var slug))
                slugs.Add(slug);
        }

        foreach (var submoduleUrl in GetRepoSubmoduleUrls(repoPath))
        {
            if (TryGetRepoSlug(submoduleUrl, out var slug))
                slugs.Add(slug);
        }

        if (slugs.Count == 0)
            return [];

        var results = new List<(string Url, string Description)>();

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AgentPrimer/1.0");

            foreach (var slug in slugs)
            {
                var repoInfo = TryFetchGitHubRepoInfo(httpClient, slug);
                if (repoInfo.HasValue)
                    results.Add(repoInfo.Value);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize repo client: {ex.Message}");
        }

        return results.ToArray();
    }

    public static List<string> ListTrackedFiles(string repoPath, out bool isGitInstalled)
    {
        var files = new List<string>();
        isGitInstalled = true;

        if (!RunGitCommand(repoPath, "ls-files --full-name --recurse-submodules", out var result))
        {
            isGitInstalled = false;
            return files;
        }

        if (result.ExitCode != 0)
        {
            Console.WriteLine($"git ls-files returned exit code {result.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                Console.WriteLine(result.StandardError);

            return files;
        }

        files
            .AddRange(
                result.StandardOutput
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(trimmed => trimmed.Length > 0));

        return files;
    }

    private static string[] GetRepoRemoteUrls(string repoPath)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!RunGitCommand(repoPath, "remote -v", out var result))
        {
            return [];
        }

        if (!result.IsSuccess)
            return [];

        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                urls.Add(parts[1]);
        }

        return urls.ToArray();
    }

    private static string[] GetRepoSubmoduleUrls(string repoPath)
    {
        var gitmodulesPath = Path.Combine(repoPath, ".gitmodules");
        if (!File.Exists(gitmodulesPath))
            return [];

        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var line in File.ReadLines(gitmodulesPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("url =", StringComparison.OrdinalIgnoreCase))
                    continue;

                var equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex < 0 || equalsIndex == trimmed.Length - 1)
                    continue;

                var url = trimmed[(equalsIndex + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(url))
                    urls.Add(url);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($".gitmodules could not be read: {ex.Message}");
        }

        return urls.ToArray();
    }

    private static bool TryGetRepoSlug(string url, out string slug)
    {
        slug = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var trimmed = url.Trim();
        string candidate;

        if (trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            candidate = trimmed["git@github.com:".Length..];
        }
        else if (trimmed.StartsWith("ssh://git@github.com/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = trimmed["ssh://git@github.com/".Length..];
        }
        else
        {
            var index = trimmed.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            candidate = trimmed[(index + "github.com/".Length)..];
        }

        candidate = candidate.Trim('/');
        if (candidate.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            candidate = candidate[..^4];

        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        slug = $"{segments[0]}/{segments[1]}";
        return true;
    }

    private static (string Url, string Description)? TryFetchGitHubRepoInfo(HttpClient httpClient, string slug)
    {
        try
        {
            using var response = httpClient.GetAsync($"https://api.github.com/repos/{slug}").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;

            using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(stream);

            var root = document.RootElement;
            var repoUrl = root.TryGetProperty("html_url", out var htmlUrl)
                ? htmlUrl.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                var fullName = root.TryGetProperty("full_name", out var fullNameElement)
                    ? fullNameElement.GetString()
                    : null;
                repoUrl = string.IsNullOrWhiteSpace(fullName)
                    ? $"https://github.com/{slug}"
                    : $"https://github.com/{fullName}";
            }

            var description = root.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString()
                : null;

            var formattedDescription = string.IsNullOrWhiteSpace(description)
                ? "Description unavailable."
                : description!.Split(['\r', '\n'])[0].Trim();

            return (repoUrl!, formattedDescription);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"Failed to retrieve GitHub metadata for '{slug}': {ex.Message}");
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse GitHub metadata for '{slug}': {ex.Message}");
        }

        return null;
    }

    private static bool RunGitCommand(string repoPath, string arguments, out ProcessCaptureResult result)
    {
        try
        {
            result = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = repoPath
            }.RunAndCaptureOutput();
            return result != null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to execute git '{arguments}': {ex.Message}");
            result = null;
            return false;
        }
    }
}

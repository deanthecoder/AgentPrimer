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

using System.Text.Json;
using System.Xml.Linq;

namespace AgentPrimer.Utilities;

/// <summary>
/// Resolves NuGet package usage within a repository, hydrating descriptions from cache or nuget.org.
/// </summary>
internal static class NugetPackageAnalyzer
{
    public static (string Name, string Description)[] AnalyzeNugetPackages(string rootDirectory, List<string> nugetCache)
    {
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectFile in RepositoryFileEnumerator.EnumerateFilesSafe(rootDirectory, "*.csproj"))
        foreach (var packageId in GetPackagesFromProject(projectFile))
            packageIds.Add(packageId);

        foreach (var packagesConfig in RepositoryFileEnumerator.EnumerateFilesSafe(rootDirectory, "packages.config"))
        foreach (var packageId in GetPackagesFromPackagesConfig(packagesConfig))
            packageIds.Add(packageId);
        
        // Exclude System.*, Microsoft.*
        packageIds = packageIds.Where(o => !o.StartsWith("System.") && !o.StartsWith("Microsoft.")).ToHashSet();
        
        // Exclude multiple packages with the same prefix.
        packageIds = packageIds.Where(o => !packageIds.Any(name => o.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (packageIds.Count == 0)
            return [];

        var result =
            nugetCache
                .Select(o =>
                {
                    var index = o.IndexOf('|');
                    var name = o[..index];
                    var description = o[(index + 1)..];
                    return (name, description);
                })
                .Where(o => packageIds.Contains(o.name))
                .ToDictionary(o => o.name, o => o.description, StringComparer.OrdinalIgnoreCase);

        var unknownPackageIds = packageIds.Except(result.Keys).ToArray();
        if (unknownPackageIds.Length > 0)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AgentPrimer/1.0");

            foreach (var packageId in unknownPackageIds)
            {
                var description = TryGetNugetDescription(httpClient, packageId);
                if (string.IsNullOrWhiteSpace(description))
                    description = "Description unavailable.";
                result[packageId] = description;
                nugetCache.Add($"{packageId}|{description}");
            }
        }

        return result
            .OrderBy(o => o.Key)
            .Select(o => (Name: o.Key, Description: o.Value))
            .ToArray();
    }

    private static string[] GetPackagesFromProject(string projectPath)
    {
        var packages = new List<string>();

        try
        {
            var document = XDocument.Load(projectPath);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;

            foreach (var packageReference in document.Descendants(ns + "PackageReference"))
            {
                var include = packageReference.Attribute("Include")?.Value;
                if (!string.IsNullOrWhiteSpace(include))
                    packages.Add(include);

                var update = packageReference.Attribute("Update")?.Value;
                if (!string.IsNullOrWhiteSpace(update))
                    packages.Add(update);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Failed to parse project file '{projectPath}': {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error parsing project file '{projectPath}': {ex.Message}");
        }

        return packages.ToArray();
    }

    private static string[] GetPackagesFromPackagesConfig(string packagesConfigPath)
    {
        try
        {
            var document = XDocument.Load(packagesConfigPath);
            return document.Descendants("package")
                .Select(o => o.Attribute("id")?.Value)
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Failed to parse packages.config '{packagesConfigPath}': {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error parsing packages.config '{packagesConfigPath}': {ex.Message}");
        }

        return [];
    }

    private static string TryGetNugetDescription(HttpClient httpClient, string packageId)
    {
        try
        {
            var url = $"https://azuresearch-usnc.nuget.org/query?q=packageid:{Uri.EscapeDataString(packageId)}&prerelease=false&semVerLevel=2.0.0";
            using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return string.Empty;

            using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("data", out var dataElement))
                return string.Empty;

            foreach (var packageElement in dataElement.EnumerateArray())
            {
                if (!packageElement.TryGetProperty("id", out var idElement))
                    continue;

                var id = idElement.GetString();
                if (!string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (packageElement.TryGetProperty("description", out var descriptionElement))
                    return descriptionElement.GetString()?.Split('\n', '\r')[0] ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"Failed to retrieve metadata for '{packageId}': {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error retrieving metadata for '{packageId}': {ex.Message}");
        }

        return string.Empty;
    }
}

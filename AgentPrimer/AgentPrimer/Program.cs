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
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using DTC.Core.Extensions;

namespace AgentPrimer;

internal static class Program
{
    public static int Main()
    {
        using var appSettings = new AppSettings();

        var repoPath = Directory.GetCurrentDirectory();

        // Locate the git repository root (walking up the directory tree if needed).
        var currentPath = repoPath;
        while (!IsGitRepository(currentPath))
        {
            var parent = Directory.GetParent(currentPath)?.FullName;
            if (parent is null)
            {
                Console.WriteLine("This script must be run from a git repository.");
                return 1;
            }

            currentPath = parent;
        }

        if (!string.Equals(repoPath, currentPath, StringComparison.OrdinalIgnoreCase))
            repoPath = currentPath;

        WriteHeader("== Repo Snapshot ==");
        WriteSectionTitle("Path:");
        Console.WriteLine($"  {repoPath}");
        Console.WriteLine();

        // Identify GitHub repositories from git remotes and submodules.
        var githubRepositories = GetGitHubRepositories(repoPath);
        if (githubRepositories.Length > 0)
        {
            WriteSectionTitle($"GitHub ({githubRepositories.Length}):");
            foreach (var repo in githubRepositories)
                Console.WriteLine($"  - {repo.Url} : {repo.Description}");
            Console.WriteLine();
        }

        // Use git to list all the source files (including submodules).
        var sourceFiles = GitListFiles(repoPath, out var isGitInstalled);
        if (!isGitInstalled)
        {
            Console.WriteLine("Git is not installed on this machine.");
            return 1;
        }

        WriteSectionTitle("Stats:");
        Console.WriteLine($"  Files      : {sourceFiles.Count}");

        // Analyze the source file types (C#, C/C++, JavaScript, Python, etc.).
        var fileTypes = AnalyzeFileTypes(sourceFiles);
        if (fileTypes.Count == 0)
        {
            Console.WriteLine("  Languages  : none");
            Console.WriteLine();
            return 1;
        }
        Console.WriteLine($"  Languages  : {fileTypes.Select(o => $"{o.Key} ({o.Value:P0})").ToCsv().Replace(",", " | ")}");
        Console.WriteLine();

        // Find all nuget package references. (Brief descriptions obtained from nuget.org)
        var nugetNameAndDescriptions = AnalyzeNugetPackages(repoPath, appSettings.NugetCache);
        WriteSectionTitle($"NuGet ({nugetNameAndDescriptions.Length}):");
        if (nugetNameAndDescriptions.Length == 0)
        {
            Console.WriteLine("  none");
        }
        else
        {
            var names = nugetNameAndDescriptions
                .Select(i => i.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PrintWrappedList(names, indent: "  ", maxWidth: 60, separator: " | ");
        }
        Console.WriteLine();

        // Preferences (nullable, test framework, mocking, UI)
        var containsCSharp = fileTypes.ContainsKey("C#");
        bool? isNullableTypesEnabled = containsCSharp ? GetIsNullableReferenceTypesEnabled(repoPath) : null;
        UnitTestFramework? unitTestFramework = containsCSharp ? GetPreferredUnitTestFramework(nugetNameAndDescriptions) : null;
        MockingFramework? mockingFramework = containsCSharp ? GetPreferredMockingFramework(nugetNameAndDescriptions) : null;
        var uiLibraries = containsCSharp ? GetPreferredUiLibraries(repoPath) : null;

        WriteSectionTitle("Preferences:");
        if (isNullableTypesEnabled.HasValue)
            Console.WriteLine($"  Nullable   : {((bool)isNullableTypesEnabled ? "enabled (most)" : "disabled (most)")}");
        if (unitTestFramework.HasValue)
            Console.WriteLine($"  Tests      : {unitTestFramework}");
        if (mockingFramework.HasValue)
            Console.WriteLine($"  Mocking    : {mockingFramework}");
        if (uiLibraries != null)
            Console.WriteLine($"  UI         : {uiLibraries.Select(o => o.ToString()).ToCsv(addSpace: true)}");
        Console.WriteLine();

        // Examine the project dependency graph, finding the top-level projects and internal projects.
        var internalProjectReferences = containsCSharp ? GetInternalProjectReferences(repoPath) : null;
        if (internalProjectReferences is { Length: > 0 })
        {
            var utilities = internalProjectReferences.Where(o => o.ReferenceCount > 0).ToArray();
            var topLevel = internalProjectReferences.Where(o => o.ReferenceCount == 0).ToArray();

            WriteSectionTitle("Projects:");
            if (topLevel.Length > 0)
            {
                var topList = string.Join(", ", topLevel.Select(l => $"{l.Name} ({l.TargetFramework})"));
                Console.WriteLine($"  Top-level  : {topList}");
            }
            if (utilities.Length > 0)
            {
                Console.WriteLine("  Internal   : " + (utilities.Length > 0 ? $"{utilities[0].Name} ({utilities[0].TargetFramework}) [refs:{utilities[0].ReferenceCount}]" : ""));
                foreach (var library in utilities.Skip(1))
                    Console.WriteLine($"               {library.Name} ({library.TargetFramework}) [refs:{library.ReferenceCount}]");
            }
            Console.WriteLine();
        }

        // List relative paths to README.md files.
        var readmeFiles = EnumerateFilesSafe(repoPath, "README.md");
        if (readmeFiles.Length > 0)
        {
            WriteSectionTitle("READMEs:");
            foreach (var readmeFile in readmeFiles)
            {
                var relativePath = Path.GetRelativePath(repoPath, readmeFile);
                Console.WriteLine($"  {relativePath}");
            }
        }

        return 0;
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
    }

    private static (string Url, string Description)[] GetGitHubRepositories(string repoPath)
    {
        var slugs = new List<string>();

        foreach (var remoteUrl in GetGitRemoteUrls(repoPath))
        {
            if (TryGetGitHubSlug(remoteUrl, out var slug))
                slugs.Add(slug);
        }

        foreach (var submoduleUrl in GetGitSubmoduleUrls(repoPath))
        {
            if (TryGetGitHubSlug(submoduleUrl, out var slug))
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
            Console.WriteLine($"Failed to initialize GitHub client: {ex.Message}");
        }

        return results.ToArray();
    }

    private static string[] GetGitRemoteUrls(string repoPath)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "remote -v",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return [];

            var standardOutput = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                return [];

            foreach (var line in standardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    urls.Add(parts[1]);
            }
        }
        catch (Win32Exception)
        {
            return [];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read git remotes: {ex.Message}");
        }

        return urls.ToArray();
    }

    private static string[] GetGitSubmoduleUrls(string repoPath)
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

    private static bool TryGetGitHubSlug(string url, out string slug)
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

    private static List<string> GitListFiles(string repoPath, out bool isGitInstalled)
    {
        var files = new List<string>();
        isGitInstalled = true;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "ls-files --full-name --recurse-submodules",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                isGitInstalled = false;
                return files;
            }

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"git ls-files returned exit code {process.ExitCode}.");
                if (!string.IsNullOrWhiteSpace(standardError))
                    Console.WriteLine(standardError.Trim());

                return files;
            }

            foreach (var line in standardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    files.Add(trimmed);
            }
        }
        catch (Win32Exception)
        {
            isGitInstalled = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to execute git ls-files: {ex.Message}");
        }

        return files;
    }

    private static Dictionary<string, double> AnalyzeFileTypes(IReadOnlyCollection<string> sourceFiles)
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

    private static UnitTestFramework GetPreferredUnitTestFramework((string Name, string Description)[] packages)
    {
        if (packages is null || packages.Length == 0)
            return UnitTestFramework.Unknown;

        var scores = new Dictionary<UnitTestFramework, int>
        {
            [UnitTestFramework.NUnit] = 0, [UnitTestFramework.XUnit] = 0, [UnitTestFramework.MSTest] = 0
        };

        foreach (var package in packages)
        {
            var name = package.Name.ToLowerInvariant();

            if (name.Contains("nunit"))
                scores[UnitTestFramework.NUnit]++;

            if (name.Contains("xunit"))
                scores[UnitTestFramework.XUnit]++;

            if (name.Contains("mstest"))
                scores[UnitTestFramework.MSTest]++;
        }

        var topScore = scores.OrderByDescending(o => o.Value).First();
        if (topScore.Value == 0)
            return UnitTestFramework.Unknown;

        var sameScoreCount = scores.Count(o => o.Value == topScore.Value);
        return sameScoreCount > 1 ? UnitTestFramework.Unknown : topScore.Key;
    }

    private static MockingFramework GetPreferredMockingFramework((string Name, string Description)[] packages)
    {
        if (packages is null || packages.Length == 0)
            return MockingFramework.Unknown;

        var scores = new Dictionary<MockingFramework, int>
        {
            [MockingFramework.Moq] = 0, [MockingFramework.FakeItEasy] = 0, [MockingFramework.NSubstitute] = 0
        };

        foreach (var package in packages)
        {
            var name = package.Name.ToLowerInvariant();

            if (name.Contains("moq"))
                scores[MockingFramework.Moq]++;

            if (name.Contains("fakeiteasy"))
                scores[MockingFramework.FakeItEasy]++;

            if (name.Contains("nsubstitute"))
                scores[MockingFramework.NSubstitute]++;
        }

        var topScore = scores.OrderByDescending(o => o.Value).First();
        if (topScore.Value == 0)
            return MockingFramework.Unknown;

        var sameScoreCount = scores.Count(o => o.Value == topScore.Value);
        return sameScoreCount > 1 ? MockingFramework.Unknown : topScore.Key;
    }

    private static UiLibrary[] GetPreferredUiLibraries(string rootDirectory)
    {
        var popularity = new Dictionary<UiLibrary, int>
        {
            [UiLibrary.Avalonia] = EnumerateFilesSafe(rootDirectory, "*.axaml").Length,
            [UiLibrary.WPF] = EnumerateFilesSafe(rootDirectory, "*.xaml").Length,
            [UiLibrary.WinForms] = AnyProjectUsesWindowsForms(rootDirectory) ? 1 : 0
        };

        var preferred = popularity
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Key == UiLibrary.WinForms ? 0 : kv.Value)
            .ThenBy(kv => kv.Key == UiLibrary.WinForms ? 1 : 0)
            .Select(kv => kv.Key)
            .ToArray();

        return preferred.Length == 0 ? [UiLibrary.Unknown] : preferred;
    }

    /// <summary>
    /// Builds a project dependency graph using ProjectReference links and counts how often each project is reached from top-level entry points.
    /// Each edge traversal increments the referenced project, so shared libraries accumulate counts from every path while target frameworks are captured for reporting.
    /// </summary>
    private static (string Name, int ReferenceCount, string TargetFramework)[] GetInternalProjectReferences(string rootDirectory)
    {
        var projectFiles = EnumerateFilesSafe(rootDirectory, "*.csproj");
        if (projectFiles.Length == 0)
            return [];

        var projectInfos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dependencyMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var referencedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectFile in projectFiles)
        {
            var projectName = Path.GetFileName(projectFile);
            if (!dependencyMap.ContainsKey(projectName))
                dependencyMap[projectName] = new List<string>();

            try
            {
                var document = XDocument.Load(projectFile);
                var ns = document.Root?.Name.Namespace ?? XNamespace.None;

                var targetFramework = document
                    .Descendants(ns + "TargetFramework")
                    .Select(e => e.Value.Trim())
                    .FirstOrDefault(value => !string.IsNullOrEmpty(value));

                if (string.IsNullOrEmpty(targetFramework))
                {
                    var targetFrameworksValue = document
                        .Descendants(ns + "TargetFrameworks")
                        .Select(e => e.Value.Trim())
                        .FirstOrDefault(value => !string.IsNullOrEmpty(value));

                    if (!string.IsNullOrEmpty(targetFrameworksValue))
                    {
                        targetFramework = targetFrameworksValue
                            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .FirstOrDefault();
                    }
                }

                if (string.IsNullOrEmpty(targetFramework))
                    targetFramework = "unknown";

                projectInfos[projectName] = targetFramework;

                foreach (var projectReference in document.Descendants(ns + "ProjectReference"))
                {
                    var include = projectReference.Attribute("Include")?.Value;
                    if (include?.Contains(".csproj", StringComparison.OrdinalIgnoreCase) != true)
                        continue;

                    var normalizedInclude = include.Replace('\\', '/');
                    var referenceName = Path.GetFileName(normalizedInclude);
                    if (string.IsNullOrEmpty(referenceName))
                        continue;

                    dependencyMap[projectName].Add(referenceName);
                    referencedProjects.Add(referenceName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                Console.WriteLine($"Failed to read '{projectFile}': {ex.Message}");
            }

            projectInfos.TryAdd(projectName, "unknown");
        }

        foreach (var projectName in projectInfos.Keys)
        {
            if (!dependencyMap.ContainsKey(projectName))
                dependencyMap[projectName] = new List<string>();
        }

        var roots = projectInfos.Keys.Where(name => !referencedProjects.Contains(name)).ToArray();
        if (roots.Length == 0)
            roots = projectInfos.Keys.ToArray();

        var usageCounts = projectInfos.Keys.ToDictionary(name => name, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                root
            };
            TraverseDependencies(root, dependencyMap, usageCounts, path);
        }

        return usageCounts
            .Select(kv => (
                Name: kv.Key,
                ReferenceCount: kv.Value,
                TargetFramework: projectInfos.GetValueOrDefault(kv.Key, "unknown")))
            .OrderByDescending(o => o.ReferenceCount)
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void TraverseDependencies(
        string projectName,
        IReadOnlyDictionary<string, List<string>> dependencyMap,
        IDictionary<string, int> usageCounts,
        HashSet<string> path)
    {
        if (!dependencyMap.TryGetValue(projectName, out var dependencies))
            return;

        foreach (var dependency in dependencies)
        {
            if (!usageCounts.ContainsKey(dependency))
                continue;

            usageCounts[dependency] = usageCounts[dependency] + 1;

            if (path.Add(dependency))
            {
                TraverseDependencies(dependency, dependencyMap, usageCounts, path);
                path.Remove(dependency);
            }
        }
    }

    private static bool AnyProjectUsesWindowsForms(string rootDirectory)
    {
        foreach (var projectPath in EnumerateFilesSafe(rootDirectory, "*.csproj"))
        {
            try
            {
                var document = XDocument.Load(projectPath);
                var ns = document.Root?.Name.Namespace ?? XNamespace.None;

                var hasWinForms = document
                    .Descendants(ns + "UseWindowsForms")
                    .Select(o => o.Value.Trim())
                    .Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

                if (hasWinForms)
                    return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                Console.WriteLine($"Failed to inspect WindowsForms setting in '{projectPath}': {ex.Message}");
            }
        }

        return false;
    }

    private static bool GetIsNullableReferenceTypesEnabled(string rootDirectory)
    {
        var projectFiles = EnumerateFilesSafe(rootDirectory, "*.csproj");
        if (projectFiles.Length == 0)
            return true;

        var disabledCount = 0;
        foreach (var projectFile in projectFiles)
        {
            if (DoesProjectDisableNullable(projectFile))
                disabledCount++;
        }

        return disabledCount * 2 <= projectFiles.Length;
    }

    private static bool DoesProjectDisableNullable(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;

            return document
                .Descendants(ns + "Nullable")
                .Select(o => o.Value.Trim())
                .Any(value => string.Equals(value, "disable", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Console.WriteLine($"Failed to inspect nullable setting in '{projectPath}': {ex.Message}");
        }

        return false;
    }

    private static (string Name, string Description)[] AnalyzeNugetPackages(string rootDirectory, List<string> nugetCache)
    {
        var packageIds = new HashSet<string>();

        foreach (var projectFile in EnumerateFilesSafe(rootDirectory, "*.csproj"))
        foreach (var packageId in GetPackagesFromProject(projectFile))
            packageIds.Add(packageId);

        foreach (var packagesConfig in EnumerateFilesSafe(rootDirectory, "packages.config"))
        foreach (var packageId in GetPackagesFromPackagesConfig(packagesConfig))
            packageIds.Add(packageId);

        if (packageIds.Count == 0)
            return [];

        // Remove names that are a longer variant of another. E.g., Remove 'Avalonia.Skia' if 'Avalonia' is in the cache.
        packageIds = packageIds.Where(o => !packageIds.Any(name => o.StartsWith(name + "."))).ToHashSet();

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
                .ToDictionary(o => o.name, o => o.description);

        var unknownPackageIds = packageIds.Except(result.Keys).ToArray();
        if (unknownPackageIds.Length > 0)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AgentPrimer/1.0");

            foreach (var packageId in unknownPackageIds)
            {
                var description = TryGetNugetDescription(httpClient, packageId);
                result[packageId] = string.IsNullOrWhiteSpace(description) ? "Description unavailable." : description;
                nugetCache.Add($"{packageId}|{description}");
            }
        }

        return result
            .OrderBy(o => o.Key)
            .Select(o => (Name: o.Key, Description: o.Value))
            .ToArray();
    }

    private static string[] EnumerateFilesSafe(string rootDirectory, string searchPattern)
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
            return document.Descendants("package").Select(o => o.Attribute("id")?.Value).Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();
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

    private static void PrintWrappedList(IEnumerable<string> items, string indent, int maxWidth, string separator = " | ")
    {
        if (items is null)
        {
            Console.WriteLine(indent + "none");
            return;
        }

        var line = indent;
        var firstInLine = true;

        foreach (var item in items)
        {
            var token = firstInLine ? item : separator + item;

            if (line.Length + token.Length > maxWidth)
            {
                // Flush current line and start a new one
                Console.WriteLine(line);
                line = indent + item;
                firstInLine = false;
                continue;
            }

            line += token;
            firstInLine = false;
        }

        if (!string.IsNullOrWhiteSpace(line.Trim()))
            Console.WriteLine(line);
    }

    // Helper methods for colorized section titles
    private static void WriteHeader(string text)
    {
        var prev = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(text);
        }
        finally
        {
            Console.ForegroundColor = prev;
        }
    }

    private static void WriteSectionTitle(string text)
    {
        var prev = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(text);
        }
        finally
        {
            Console.ForegroundColor = prev;
        }
    }
}
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

using System.Xml.Linq;

namespace AgentPrimer.Utilities;

/// <summary>
/// Analyzes repository projects to infer language, testing, and UI preferences.
/// </summary>
internal static class ProjectPreferencesAnalyzer
{
    public static UnitTestFramework GetPreferredUnitTestFramework((string Name, string Description)[] packages)
    {
        if (packages is null || packages.Length == 0)
            return UnitTestFramework.Unknown;

        var scores = new Dictionary<UnitTestFramework, int>
        {
            [UnitTestFramework.NUnit] = 0,
            [UnitTestFramework.XUnit] = 0,
            [UnitTestFramework.MSTest] = 0
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

    public static MockingFramework GetPreferredMockingFramework((string Name, string Description)[] packages)
    {
        if (packages is null || packages.Length == 0)
            return MockingFramework.Unknown;

        var scores = new Dictionary<MockingFramework, int>
        {
            [MockingFramework.Moq] = 0,
            [MockingFramework.FakeItEasy] = 0,
            [MockingFramework.NSubstitute] = 0
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

    public static UiLibrary[] GetPreferredUiLibraries(string rootDirectory)
    {
        var popularity = new Dictionary<UiLibrary, int>
        {
            [UiLibrary.Avalonia] = RepositoryFileEnumerator.EnumerateFilesSafe(rootDirectory, "*.axaml").Length,
            [UiLibrary.WPF] = RepositoryFileEnumerator.EnumerateFilesSafe(rootDirectory, "*.xaml").Length,
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

    public static bool GetIsNullableReferenceTypesEnabled(string rootDirectory)
    {
        var projectFiles = RepositoryFileEnumerator.EnumerateFilesSafe(rootDirectory, "*.csproj");
        if (projectFiles.Length == 0)
            return false;

        var disabledCount = projectFiles.Count(DoesProjectDisableNullable);
        return disabledCount * 2 <= projectFiles.Length;
    }

    public static (string Name, int ReferenceCount, string TargetFramework)[] GetInternalProjectReferences(string rootDirectory)
    {
        var projectFiles = RepositoryFileEnumerator.EnumerateFilesSafe(rootDirectory, "*.csproj");
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
                    .Union(document.Descendants(ns + "TargetFrameworkVersion"))
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

    private static bool AnyProjectUsesWindowsForms(string rootDirectory)
    {
        foreach (var projectPath in RepositoryFileEnumerator.EnumerateFilesSafe(rootDirectory, "*.csproj"))
        {
            try
            {
                var document = XDocument.Load(projectPath);
                var ns = document.Root?.Name.Namespace ?? XNamespace.None;

                var hasWpf = document
                    .Descendants(ns + "UseWPF")
                    .Select(o => o.Value.Trim())
                    .Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
                if (hasWpf)
                    return false; // Not WinForms.

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

    private static bool DoesProjectDisableNullable(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;

            // Default is disabled, unless we find <Nullable>enable</Nullable>
            return !document
                .Descendants(ns + "Nullable")
                .Select(o => o.Value.Trim())
                .Any(value => string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Console.WriteLine($"Failed to inspect nullable setting in '{projectPath}': {ex.Message}");
        }

        return false;
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
}

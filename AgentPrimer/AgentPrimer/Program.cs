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

using AgentPrimer.Utilities;
using DTC.Core.Extensions;

namespace AgentPrimer;

internal static class Program
{
    public static int Main()
    {
        using var appSettings = new AppSettings();

        var currentDirectory = Directory.GetCurrentDirectory();
        if (!RepositoryLocator.TryFindGitRoot(currentDirectory, out var repoPath))
        {
            Console.WriteLine("This script must be run from a git repository.");
            return 1;
        }

        ConsoleSectionWriter.WriteHeader("== Repo Snapshot ==");
        ConsoleSectionWriter.WriteSectionTitle("Path:");
        Console.WriteLine($"  {repoPath}");
        Console.WriteLine();

        // Identify GitHub repositories from git remotes and submodules.
        var githubRepositories = GitRepositoryInspector.GetGitHubRepositories(repoPath);
        if (githubRepositories.Length > 0)
        {
            ConsoleSectionWriter.WriteSectionTitle($"GitHub ({githubRepositories.Length}):");
            foreach (var repo in githubRepositories)
                Console.WriteLine($"  - {repo.Url} : {repo.Description}");
            Console.WriteLine();
        }

        // Use git to list all the source files (including submodules).
        var sourceFiles = GitRepositoryInspector.ListTrackedFiles(repoPath, out var isGitInstalled);
        if (!isGitInstalled)
        {
            Console.WriteLine("Git is not installed on this machine.");
            return 1;
        }

        ConsoleSectionWriter.WriteSectionTitle("Stats:");
        Console.WriteLine($"  Files      : {sourceFiles.Count}");

        // Analyze the source file types (C#, C/C++, JavaScript, Python, etc.).
        var fileTypes = SourceFileAnalyzer.AnalyzeFileTypes(sourceFiles);
        if (fileTypes.Count == 0)
        {
            Console.WriteLine("  Languages  : none");
            Console.WriteLine();
            return 1;
        }
        Console.WriteLine($"  Languages  : {fileTypes.Select(o => $"{o.Key} ({o.Value:P0})").ToCsv().Replace(",", " | ")}");
        var englishPreference = EnglishPreferenceAnalyzer.DeterminePreferredEnglishVariant(repoPath, sourceFiles);
        Console.WriteLine($"  English    : {englishPreference} English");
        Console.WriteLine();

        // Find all nuget package references. (Brief descriptions obtained from nuget.org)
        var nugetNameAndDescriptions = NugetPackageAnalyzer.AnalyzeNugetPackages(repoPath, appSettings.NugetCache);
        ConsoleSectionWriter.WriteSectionTitle($"NuGet ({nugetNameAndDescriptions.Length}):");
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
            ConsoleSectionWriter.PrintWrappedList(names, indent: "  ", maxWidth: 80, separator: " | ");
        }
        Console.WriteLine();

        // Preferences (nullable, test framework, mocking, UI)
        var containsCSharp = fileTypes.ContainsKey("C#");
        bool? isNullableTypesEnabled = containsCSharp ? ProjectPreferencesAnalyzer.GetIsNullableReferenceTypesEnabled(repoPath) : null;
        UnitTestFramework? unitTestFramework = containsCSharp ? ProjectPreferencesAnalyzer.GetPreferredUnitTestFramework(nugetNameAndDescriptions) : null;
        MockingFramework? mockingFramework = containsCSharp ? ProjectPreferencesAnalyzer.GetPreferredMockingFramework(nugetNameAndDescriptions) : null;
        var uiLibraries = containsCSharp ? ProjectPreferencesAnalyzer.GetPreferredUiLibraries(repoPath) : null;

        ConsoleSectionWriter.WriteSectionTitle("Preferences:");
        if (isNullableTypesEnabled.HasValue)
            Console.WriteLine($"  Nullable   : {((bool)isNullableTypesEnabled ? "enabled" : "disabled")}");
        if (unitTestFramework.HasValue)
            Console.WriteLine($"  Tests      : {unitTestFramework}");
        if (mockingFramework.HasValue)
            Console.WriteLine($"  Mocking    : {mockingFramework}");
        if (uiLibraries != null)
            Console.WriteLine($"  UI         : {uiLibraries.Select(o => o.ToString()).ToCsv(addSpace: true)}");
        Console.WriteLine();

        // Examine the project dependency graph, finding the top-level projects and internal projects.
        var internalProjectReferences = containsCSharp ? ProjectPreferencesAnalyzer.GetInternalProjectReferences(repoPath) : null;
        if (internalProjectReferences is { Length: > 0 })
        {
            var utilities = internalProjectReferences.Where(o => o.ReferenceCount > 0).ToArray();
            var topLevel = internalProjectReferences.Where(o => o.ReferenceCount == 0).ToArray();

            ConsoleSectionWriter.WriteSectionTitle("Projects:");
            if (topLevel.Length > 0)
            {
                Console.WriteLine($"  Top-level  : {topLevel[0].Name} ({topLevel[0].TargetFramework})");
                foreach (var project in topLevel.Skip(1))
                    Console.WriteLine($"               {project.Name} ({project.TargetFramework})");
            }
            if (utilities.Length > 0)
            {
                Console.WriteLine($"  Internal   : {utilities[0].Name} ({utilities[0].TargetFramework}) [refs:{utilities[0].ReferenceCount}]");
                foreach (var library in utilities.Skip(1))
                    Console.WriteLine($"               {library.Name} ({library.TargetFramework}) [refs:{library.ReferenceCount}]");
            }
            Console.WriteLine();
        }

        // List relative paths to README.md files.
        var readmeFiles = RepositoryFileEnumerator.EnumerateFilesSafe(repoPath, "README.md");
        if (readmeFiles.Length > 0)
        {
            ConsoleSectionWriter.WriteSectionTitle("READMEs:");
            foreach (var readmeFile in
                     readmeFiles
                         .Where(o => !o.Contains("/bin/") && !o.Contains("\\bin\\") && !o.Contains("/obj/") &&
                                     !o.Contains("\\obj\\") && !o.Contains("/packages/") && !o.Contains("\\packages\\"))
                         .OrderBy(o => o))
            {
                var relativePath = Path.GetRelativePath(repoPath, readmeFile);
                Console.WriteLine($"  {relativePath}");
            }
        }

        return 0;
    }
}

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

namespace AgentPrimer;

internal static class Program
{
    public static int Main()
    {
        using var appSettings = new AppSettings();
        var output = new ConsoleReportOutput();

        var currentDirectory = Directory.GetCurrentDirectory();
        if (!RepositoryLocator.TryFindGitRoot(currentDirectory, out var repoPath))
            return output.WriteRepositoryNotFound();

        // Identify GitHub repositories from git remotes and submodules.
        var repositories = GitRepositoryInspector.GetRepositories(repoPath);

        // Use git to list all the source files (including submodules).
        var sourceFiles = GitRepositoryInspector.ListTrackedFiles(repoPath, out var isGitInstalled);
        if (!isGitInstalled)
            return output.WriteGitUnavailable();

        var fileTypes = SourceFileAnalyzer.AnalyzeFileTypes(sourceFiles);
        var containsCSharp = fileTypes.ContainsKey("C#");

        var englishPreference = fileTypes.Count > 0
            ? EnglishPreferenceAnalyzer.DeterminePreferredEnglishVariant(repoPath, sourceFiles)
            : EnglishPreferenceAnalyzer.EnglishVariant.American;

        (string Name, string Description)[] nugetNameAndDescriptions = [];
        if (fileTypes.Count > 0)
            nugetNameAndDescriptions = NugetPackageAnalyzer.AnalyzeNugetPackages(repoPath, appSettings.NugetCache);

        // Preferences (nullable, test framework, mocking, UI)
        bool? isNullableTypesEnabled = containsCSharp
            ? ProjectPreferencesAnalyzer.GetIsNullableReferenceTypesEnabled(repoPath)
            : null;
        UnitTestFramework? unitTestFramework = containsCSharp
            ? ProjectPreferencesAnalyzer.GetPreferredUnitTestFramework(nugetNameAndDescriptions)
            : null;
        MockingFramework? mockingFramework = containsCSharp
            ? ProjectPreferencesAnalyzer.GetPreferredMockingFramework(nugetNameAndDescriptions)
            : null;
        var uiLibraries = containsCSharp
            ? ProjectPreferencesAnalyzer.GetPreferredUiLibraries(repoPath)
            : null;

        // Examine the project dependency graph, finding the top-level projects and internal projects.
        var internalProjectReferences = containsCSharp
            ? ProjectPreferencesAnalyzer.GetInternalProjectReferences(repoPath)
            : [];

        // List relative paths to README.md files.
        var readmeFiles = RepositoryFileEnumerator
            .EnumerateFilesSafe(repoPath, "README.md")
            .Where(o => !o.Contains("/bin/") && !o.Contains("\\bin\\") && !o.Contains("/obj/")
                        && !o.Contains("\\obj\\") && !o.Contains("/packages/") && !o.Contains("\\packages\\"))
            .OrderBy(o => o, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(repoPath, path))
            .ToArray();

        var report = new PrimerReport
        {
            RepositoryPath = repoPath,
            Repositories = repositories,
            SourceFileCount = sourceFiles.Count,
            LanguageBreakdown = fileTypes,
            EnglishPreference = englishPreference,
            NugetPackages = nugetNameAndDescriptions,
            ContainsCSharp = containsCSharp,
            NullableReferenceTypesEnabled = isNullableTypesEnabled,
            UnitTestFramework = unitTestFramework,
            MockingFramework = mockingFramework,
            PreferredUiLibraries = uiLibraries ?? [],
            InternalProjects = internalProjectReferences,
            ReadmeFiles = readmeFiles
        };

        return output.WriteReport(report);
    }
}

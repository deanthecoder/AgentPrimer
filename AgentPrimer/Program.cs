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
using AgentPrimer.Utilities;
using DTC.Core.Markdown;

namespace AgentPrimer;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Parse command-line flags.
        if (!TryParseFlags(args, out var agentMode, out var showHelp))
            return ShowUsage();

        if (showHelp)
            return ShowUsage();
        using var appSettings = new AppSettings();
        var output = new ConsoleReportOutput(agentMode);

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

        var largestSourceFiles = GetLargestSourceFiles(repoPath, sourceFiles);
        var supportedUiLanguages = LocalizationAnalyzer.GetSupportedUiLanguages(sourceFiles);

        var report = new PrimerReport
        {
            RepositoryPath = repoPath,
            Repositories = repositories,
            SourceFileCount = sourceFiles.Count,
            LanguageBreakdown = fileTypes,
            EnglishPreference = englishPreference,
            NugetPackages = nugetNameAndDescriptions,
            NullableReferenceTypesEnabled = isNullableTypesEnabled,
            UnitTestFramework = unitTestFramework,
            MockingFramework = mockingFramework,
            PreferredUiLibraries = uiLibraries ?? [],
            SupportedUiLanguages = supportedUiLanguages,
            InternalProjects = internalProjectReferences,
            ReadmeFiles = readmeFiles,
            LargestSourceFiles = largestSourceFiles
        };

        return output.WriteReport(report);
    }

    private static (string Path, long Size)[] GetLargestSourceFiles(string repoPath, IList<string> trackedFiles)
    {
        if (trackedFiles.Count == 0)
            return [];

        var candidates = new List<(string Path, long Size)>();

        foreach (var relativePath in trackedFiles)
        {
            if (!SourceFileAnalyzer.IsRecognizedSourceExtension(Path.GetExtension(relativePath)))
                continue;

            var fullPath = Path.Combine(repoPath, relativePath);
            try
            {
                var fileInfo = new FileInfo(fullPath);
                if (!fileInfo.Exists)
                    continue;

                candidates.Add((relativePath, fileInfo.Length));
            }
            catch (IOException)
            {
                // Ignore files that cannot be inspected.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore files we cannot read.
            }
        }

        return candidates
            .OrderByDescending(f => f.Size)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static bool TryParseFlags(string[] args, out bool agentMode, out bool showHelp)
    {
        agentMode = false;
        showHelp = false;
        if (args == null || args.Length == 0)
            return true;

        foreach (var raw in args)
        {
            var a = raw?.Trim();
            if (string.IsNullOrEmpty(a))
                continue;

            if (a.Equals("/agent", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--agent", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-a", StringComparison.Ordinal))
            {
                agentMode = true;
                continue;
            }

            if (a.Equals("/?:", StringComparison.Ordinal) || // some shells pass "/?:"
                a.Equals("/?", StringComparison.Ordinal) ||
                a.Equals("/h", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-h", StringComparison.Ordinal) ||
                a.Equals("-?", StringComparison.Ordinal))
            {
                showHelp = true;
                continue;
            }

            // Unknown switch that looks like a flag => show usage.
            if (a.StartsWith('-') || a.StartsWith('/'))
                return false;
        }

        return true;
    }

    private static int ShowUsage()
    {
        var usage = new StringBuilder();
        usage
            .AppendLine("# AgentPrimer")
            .AppendLine("A quick way to teach AI tools how your codebase works.")
            .AppendLine("## Usage")
            .AppendLine("AgentPrimer [--agent | --help]")
            .AppendLine("## Examples:")
            .AppendLine("* `AgentPrimer`            Generate console report for current git repo.")
            .AppendLine("* `AgentPrimer --agent`    Include AI agent notes (also accepts /agent, -a).")
            .AppendLine("* `AgentPrimer --help`     Show this help (also accepts /?, -h).")
            .AppendLine("## Switches")
            .AppendLine("* `--agent | -a | /agent`    Enables Agent Instructions mode.")
            .AppendLine("* `--help  | -h | -? | /?`   Show this help text.");

        new ConsoleMarkdown().Write(usage.ToString());
        return 0;
    }
}

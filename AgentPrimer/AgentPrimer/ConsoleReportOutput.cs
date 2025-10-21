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

/// <summary>
/// Renders repository reports and errors to the console.
/// </summary>
internal sealed class ConsoleReportOutput
{
    public int WriteRepositoryNotFound()
    {
        Console.WriteLine("This script must be run from a git repository.");
        return 1;
    }

    public int WriteGitUnavailable()
    {
        Console.WriteLine("Git is not installed on this machine.");
        return 1;
    }

    public int WriteReport(PrimerReport report)
    {
        ConsoleSectionWriter.WriteHeader("== Repo Snapshot ==");
        ConsoleSectionWriter.WriteSectionTitle("Path:");
        Console.WriteLine($"  {report.RepositoryPath}");
        Console.WriteLine();

        if (report.Repositories.Count > 0)
        {
            ConsoleSectionWriter.WriteSectionTitle($"Repositories ({report.Repositories.Count}):");
            foreach (var repository in report.Repositories)
                Console.WriteLine($"  - {repository.Url} : {repository.Description}");
            Console.WriteLine();
        }

        ConsoleSectionWriter.WriteSectionTitle("Stats:");
        Console.WriteLine($"  Files      : {report.SourceFileCount}");

        if (report.LanguageBreakdown.Count == 0)
        {
            Console.WriteLine("  Languages  : none");
            Console.WriteLine();
            return 1;
        }

        var languageSummary = report.LanguageBreakdown
            .Select(o => $"{o.Key} ({o.Value:P0})")
            .ToCsv()
            .Replace(",", " | ", StringComparison.Ordinal);

        Console.WriteLine($"  Languages  : {languageSummary}");
        Console.WriteLine($"  English    : {report.EnglishPreference} English");
        Console.WriteLine();

        ConsoleSectionWriter.WriteSectionTitle($"NuGet ({report.NugetPackages.Count}):");
        if (report.NugetPackages.Count == 0)
        {
            Console.WriteLine("  none");
        }
        else
        {
            var names = report.NugetPackages
                .Select(i => i.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            ConsoleSectionWriter.PrintWrappedList(names, indent: "  ", maxWidth: 80, separator: " | ");
        }
        Console.WriteLine();

        ConsoleSectionWriter.WriteSectionTitle("Preferences:");
        if (report.NullableReferenceTypesEnabled.HasValue)
            Console.WriteLine($"  Nullable   : {(report.NullableReferenceTypesEnabled.Value ? "enabled" : "disabled")}");
        if (report.UnitTestFramework.HasValue)
            Console.WriteLine($"  Tests      : {report.UnitTestFramework.Value}");
        if (report.MockingFramework.HasValue)
            Console.WriteLine($"  Mocking    : {report.MockingFramework.Value}");
        if (report.PreferredUiLibraries.Length > 0)
            Console.WriteLine($"  UI         : {report.PreferredUiLibraries.Select(o => o.ToString()).ToCsv(addSpace: true)}");
        Console.WriteLine();

        if (report.InternalProjects.Count > 0)
        {
            var utilities = report.InternalProjects.Where(o => o.ReferenceCount > 0).ToArray();
            var topLevel = report.InternalProjects.Where(o => o.ReferenceCount == 0).ToArray();

            if (utilities.Length > 0 || topLevel.Length > 0)
            {
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
        }

        if (report.ReadmeFiles.Count > 0)
        {
            ConsoleSectionWriter.WriteSectionTitle("READMEs:");
            foreach (var readme in report.ReadmeFiles)
                Console.WriteLine($"  {readme}");
        }

        return 0;
    }
}

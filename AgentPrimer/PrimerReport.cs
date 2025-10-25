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

/// <summary>
/// Captures the collected repository insights ready for presentation.
/// </summary>
internal sealed class PrimerReport
{
    public required string RepositoryPath { get; init; }

    public required IReadOnlyList<(string Url, string Description, bool IsSubmodule)> Repositories { get; init; }

    public required int SourceFileCount { get; init; }

    public required IReadOnlyDictionary<string, double> LanguageBreakdown { get; init; }

    public required EnglishPreferenceAnalyzer.EnglishVariant EnglishPreference { get; init; }

    public required IReadOnlyList<(string Name, string Description)> NugetPackages { get; init; }

    public bool? NullableReferenceTypesEnabled { get; init; }

    public UnitTestFramework? UnitTestFramework { get; init; }

    public MockingFramework? MockingFramework { get; init; }

    public UiLibrary[] PreferredUiLibraries { get; init; } = [];

    public required IReadOnlyList<(string Name, int ReferenceCount, string TargetFramework)> InternalProjects { get; init; }

    public required IReadOnlyList<string> ReadmeFiles { get; init; }
    
    public (string Path, long Size)[] LargestSourceFiles { get; init; }
}

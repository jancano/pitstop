using System.Collections.Immutable;

namespace Pitstop.ProjectSystem;

public sealed record NativeConfiguration(string Mode, string Platform, string Architecture, string? Toolchain);

public sealed record NativeCompilationDatabases(string Authoritative, string Semantic);

public enum NativeLanguage { Unknown, C, Cpp }

public interface INativeTarget
{
    string Name { get; }
    string Kind { get; }
    string? Output { get; }
    ImmutableArray<string> SourceFiles { get; }
    ImmutableArray<string> HeaderFiles { get; }
    IEnumerable<NativeLanguage> CompilerLanguages { get; }
}

/// <summary>The read-only portion of an evaluated workspace used by the native language services.</summary>
public interface INativeWorkspace
{
    string Root { get; }
    string ProviderId { get; }
    string ProviderName { get; }
    NativeConfiguration Configuration { get; }
    IReadOnlyList<INativeTarget> Targets { get; }
    NativeCompilationDatabases? CompilationDatabases { get; }
    string? CompilationDatabaseProblem { get; }
    ImmutableArray<string> DependenciesOf(string target);
    string? MatchOutputTarget(string? output, IReadOnlyCollection<string> candidates);
}

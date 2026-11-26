using System.Collections.Immutable;

namespace Pitstop.Xmake;

public sealed record XmakeInstallation(string Executable, Version Version, string BuildVersion);

public sealed record XmakeConfiguration(string Name, string Mode, string Platform, string Architecture, string BuildDirectory,
    string ConfigurationFile, string? Toolchain);

public sealed record XmakeToolchain(ImmutableArray<string> Names, ImmutableArray<XmakeCompiler> Compilers, XmakeLinker? Linker);

public sealed record XmakeCompiler(string SourceKind, string Program, string? Flags, string? TargetFlags);

public sealed record XmakeLinker(string Kind, string Program, string? Flags, string? TargetFlags);

public sealed record XmakePackage(string Name);

public sealed record XmakeTarget(string Name, string Kind, string Script, string? Output,
    ImmutableArray<string> SourceFiles, ImmutableArray<string> HeaderFiles, ImmutableArray<XmakePackage> Packages, XmakeToolchain Toolchain);

public sealed record XmakeTargetGraph(ImmutableArray<string> Roots, ImmutableDictionary<string, ImmutableArray<string>> Edges)
{
    public ImmutableArray<string> DependenciesOf(string target) => Edges.GetValueOrDefault(target, []);
}

/// <summary>The untouched Xmake export and the source-oriented database derived from it for future semantic use.</summary>
public sealed record XmakeCompilationDatabases(string Authoritative, string Semantic);

public sealed record XmakeWorkspace(string Root, XmakeInstallation Installation, XmakeConfiguration Configuration,
    ImmutableArray<XmakeTarget> Targets, XmakeTargetGraph Graph, ImmutableArray<XmakePackage> Packages,
    ImmutableArray<string> ProjectFiles, XmakeCompilationDatabases? CompilationDatabases, string? CompilationDatabaseProblem);

public sealed class XmakeException(string message, Exception? inner = null) : Exception(message, inner);

using System.Collections.Immutable;

namespace Pitstop.ProjectSystem;

public enum NativeConsole { Internal, IntegratedTerminal, ExternalTerminal }

public sealed record NativeSourceMap(string BuildPath, string WorkspacePath);

public sealed record NativeLaunchOptions(
    ImmutableArray<string> Arguments = default,
    string? WorkingDirectory = null,
    ImmutableDictionary<string, string>? Environment = null,
    NativeConsole Console = NativeConsole.Internal,
    bool BuildBeforeDebug = true,
    bool StopOnEntry = false,
    ImmutableArray<NativeSourceMap> SourceMaps = default);

/// <summary>Evaluated default execution for one provider snapshot. An attached process has no owned launch plan.</summary>
public sealed record NativeLaunchPlan(INativeWorkspace Workspace, string Target, string Executable,
    string WorkingDirectory, ImmutableArray<string> Arguments, ImmutableDictionary<string, string> Environment,
    ImmutableArray<string> RuntimeLibraryPaths, NativeConsole Console, bool BuildBeforeDebug, bool StopOnEntry,
    ImmutableArray<NativeSourceMap> SourceMaps);

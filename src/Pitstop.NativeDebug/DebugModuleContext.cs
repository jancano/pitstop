using System.Collections.Immutable;
using Pitstop.Core;
using Pitstop.Modules;
using Pitstop.ProjectSystem;

namespace Pitstop.NativeDebug;

/// <summary>The scanner evidence for a source location. Multiple matches remain visible as candidates.</summary>
public sealed record DebugModuleContext(ImmutableArray<TranslationUnit> Units, ImmutableArray<ModuleProvider> Providers)
{
    public static DebugModuleContext For(ModuleWorkspace snapshot, INativeWorkspace workspace, string? source)
    {
        if (!ReferenceEquals(snapshot.Workspace, workspace) || source is null || !Path.IsPathFullyQualified(source))
        {
            return new([], []);
        }
        ImmutableArray<TranslationUnit> units = [.. snapshot.UnitsFor(PathNames.Canonical(source))];
        HashSet<int> ids = [.. units.Select(unit => unit.Id)];
        return new(units, [.. snapshot.Providers.Where(provider => ids.Contains(provider.Unit.Id))]);
    }
}

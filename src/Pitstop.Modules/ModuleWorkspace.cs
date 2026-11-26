using System.Collections.Immutable;
using Passenger.Core;
using Pitstop.Xmake;

namespace Pitstop.Modules;

public enum ModuleUnitKind { Unknown, PrimaryInterface, InterfacePartition, InternalPartition, Implementation }
public enum ModuleOrigin { Workspace, External, StandardLibrary }
public enum ModuleResolution { Resolved, Unresolved, Ambiguous }
public enum ModuleResolutionEvidence { Unavailable, XmakeCommand, ScannerSource, TargetDependencies }
public enum ModuleTargetEvidence { Unavailable, SourceMembership, XmakeOutputPath }

public sealed record ModuleContext(string? Target, string Compiler, string Standard, string CommandKey)
{
    public ModuleTargetEvidence TargetEvidence { get; init; }
    public string ConfigurationKey { get; init; } = string.Empty;
}

public sealed record ModuleIdentity(string Name, ModuleContext Context);

public sealed record TranslationUnit(int Id, string Source, string Output, ModuleContext Context);

public sealed record ModuleProvider(ModuleIdentity Identity, TranslationUnit Unit, ModuleUnitKind Kind, ModuleOrigin Origin)
{
    public string Name => Identity.Name;
    public string PrimaryName => Name.Split(':', 2)[0];
}

public sealed record ModuleDependency(TranslationUnit Consumer, string Name, string? ReportedSource,
    ModuleResolution Resolution, ImmutableArray<ModuleProvider> Candidates)
{
    public ModuleProvider? Provider => Resolution == ModuleResolution.Resolved ? Candidates[0] : null;
    public ModuleResolutionEvidence Evidence { get; init; }
}

public sealed class ModuleWorkspace
{
    private readonly ILookup<int, ModuleDependency> incoming;
    private readonly ILookup<int, ModuleDependency> outgoing;
    private readonly ILookup<string, TranslationUnit> bySource;
    private readonly ILookup<string, ModuleProvider> names;

    internal ModuleWorkspace(XmakeWorkspace workspace, ImmutableArray<TranslationUnit> units, ImmutableArray<ModuleProvider> providers,
        ImmutableArray<ModuleDependency> dependencies, ImmutableArray<string> missing, string? problem)
    {
        Workspace = workspace;
        Units = units;
        Providers = providers;
        Dependencies = dependencies;
        Missing = missing;
        Problem = problem;
        incoming = dependencies.Where(edge => edge.Provider is not null).ToLookup(edge => edge.Provider!.Unit.Id);
        outgoing = dependencies.ToLookup(edge => edge.Consumer.Id);
        bySource = units.ToLookup(unit => unit.Source, PathNames.Comparer);
        names = providers.ToLookup(provider => provider.Name, StringComparer.Ordinal);
    }

    public XmakeWorkspace Workspace { get; }
    public ImmutableArray<TranslationUnit> Units { get; }
    public ImmutableArray<ModuleProvider> Providers { get; }
    public ImmutableArray<ModuleDependency> Dependencies { get; }
    public ImmutableArray<string> Missing { get; }
    public string? Problem { get; }
    public bool IsComplete => Problem is null && Missing.IsEmpty && Dependencies.All(edge => edge.Resolution == ModuleResolution.Resolved);

    public IEnumerable<TranslationUnit> UnitsFor(string source) => bySource[source];
    public IEnumerable<ModuleProvider> Named(string name) => names[name];
    public IEnumerable<ModuleDependency> Imports(TranslationUnit unit) => outgoing[unit.Id];
    public IEnumerable<ModuleDependency> Consumers(TranslationUnit unit) => incoming[unit.Id];

    public IEnumerable<TranslationUnit> Reachable(TranslationUnit root, bool consumers)
    {
        HashSet<int> visited = [root.Id];
        Queue<TranslationUnit> pending = new();
        pending.Enqueue(root);
        while (pending.TryDequeue(out TranslationUnit? unit))
        {
            IEnumerable<TranslationUnit> adjacent = consumers ? Consumers(unit).Select(edge => edge.Consumer)
                : Imports(unit).Select(edge => edge.Provider?.Unit).OfType<TranslationUnit>();
            foreach (TranslationUnit next in adjacent)
            {
                if (visited.Add(next.Id))
                {
                    yield return next;
                    pending.Enqueue(next);
                }
            }
        }
    }

    public bool IsCyclic(TranslationUnit unit) => Reachable(unit, consumers: false).Prepend(unit)
        .Any(candidate => Imports(candidate).Any(edge => edge.Provider?.Unit.Id == unit.Id));

    public static ImmutableHashSet<string> VisibleTargets(XmakeWorkspace workspace, string? target)
    {
        if (target is null)
        {
            return ImmutableHashSet<string>.Empty;
        }
        HashSet<string> seen = new(StringComparer.Ordinal);
        Stack<string> pending = new([target]);
        while (pending.TryPop(out string? next))
        {
            if (seen.Add(next))
            {
                foreach (string dependency in workspace.Graph.DependenciesOf(next))
                {
                    pending.Push(dependency);
                }
            }
        }
        return seen.ToImmutableHashSet(StringComparer.Ordinal);
    }
}

using Passenger.App.Workspace;
using Passenger.Core;
using Pitstop.Modules;

namespace Passenger.App.Explorer;

public sealed partial class ExplorerTree
{
    private bool moduleView;
    internal bool IsModuleView => moduleView;

    internal void LoadModules(string root, ModuleWorkspaceSession index)
    {
        xmakeView = false;
        moduleView = true;
        IsSolutionView = false;
        solution = null;
        programs = [];
        Take([]);
        Show(new(NodeKind.ModuleGroup, "C++ Modules", root, 0, false, _ => ModuleChildren(index)) { ViewKey = "modules:root" });
    }

    private static List<ExplorerNode> ModuleChildren(ModuleWorkspaceSession index)
    {
        List<ExplorerNode> rows = [];
        if (index.Snapshot is not { } snapshot)
        {
            rows.Add(XmakeStatus(index.Status, null));
            return rows;
        }
        rows.Add(XmakeStatus($"{snapshot.Workspace.Configuration.Mode} · {snapshot.Workspace.Configuration.Platform} · {snapshot.Workspace.Configuration.Architecture}", index.Status));
        if (!snapshot.IsComplete)
        {
            rows.Add(XmakeStatus(snapshot.Problem is null ? "Incomplete graph" : "Module scan failed", index.Status));
        }
        foreach (IGrouping<string, ModuleProvider> group in snapshot.Providers.GroupBy(provider => provider.Origin switch
        {
            ModuleOrigin.StandardLibrary => "Standard Library",
            ModuleOrigin.External => "External Modules",
            _ => provider.Unit.Context.Target ?? "Unknown target",
        }))
        {
            rows.Add(new(NodeKind.ModuleGroup, group.Key, null, 1, false,
                _ => [.. group.GroupBy(provider => provider.PrimaryName, StringComparer.Ordinal).Select(module => ModuleRow(module.Key, group.Key, module))])
            { ViewKey = $"modules:target:{group.Key}" });
        }
        ModuleDependency[] unresolved = [.. snapshot.Dependencies.Where(edge => edge.Provider is null)];
        if (unresolved.Length + snapshot.Missing.Length > 0)
        {
            rows.Add(new(NodeKind.ModuleGroup, "Unresolved or missing", null, 1, false,
                _ => [.. unresolved.Select(edge => new ExplorerNode(NodeKind.File, edge.Name, edge.Consumer.Source, 2, false, null,
                    edge.Resolution.ToString()) { Tip = edge.Consumer.Source }),
                    .. snapshot.Missing.Select(source => new ExplorerNode(NodeKind.XmakeStatus, Path.GetFileName(source), null, 2, false, null, "Unscanned") { Tip = source })])
            { ViewKey = "modules:unresolved" });
        }
        return rows;
    }

    private static ExplorerNode ModuleRow(string name, string group, IEnumerable<ModuleProvider> providers) =>
        new(NodeKind.NamedModule, name, null, 2, false, _ => [.. providers.Select(provider =>
            new ExplorerNode(NodeKind.File, provider.Name, provider.Unit.Source, 3, false, null, ModuleLabel(provider.Kind))
            {
                Tip = provider.Unit.Source,
                ModuleKind = provider.Kind,
                ViewKey = $"modules:unit:{group}:{provider.Name}:{provider.Unit.Output}",
            })]) { ViewKey = $"modules:name:{group}:{name}" };

    internal static string ModuleLabel(ModuleUnitKind kind) => kind switch
    {
        ModuleUnitKind.PrimaryInterface => "Primary interface",
        ModuleUnitKind.InterfacePartition => "Interface partition",
        ModuleUnitKind.InternalPartition => "Internal partition",
        ModuleUnitKind.Implementation => "Implementation unit",
        _ => "Unit kind unknown",
    };

    private ExplorerNode? FindModuleFile(string path, Action<ExplorerNode> through)
    {
        if (Root is not { } root)
        {
            return null;
        }
        foreach (ExplorerNode group in root.Children ?? [])
        {
            foreach (ExplorerNode module in group.Children ?? [])
            {
                ExplorerNode? file = module.IsFile ? module : module.Children?.FirstOrDefault(row => row.Path is { } source && PathNames.Same(path, source));
                if (file?.Path is { } candidate && PathNames.Same(path, candidate))
                {
                    through(root);
                    through(group);
                    if (file != module)
                    {
                        through(module);
                    }
                    return file;
                }
            }
        }
        return null;
    }
}

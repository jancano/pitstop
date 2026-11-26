using Passenger.App.Workspace;
using Passenger.Core;
using Pitstop.Xmake;

namespace Passenger.App.Explorer;

public sealed partial class ExplorerTree
{
    private bool xmakeView;

    internal bool IsXmakeView => xmakeView;

    internal void LoadXmake(string root, XmakeWorkspaceSession native)
    {
        xmakeView = true;
        IsSolutionView = false;
        solution = null;
        programs = [];
        Take([]);
        XmakeWorkspace? snapshot = native.Snapshot;
        string title = snapshot?.Configuration.Name ?? Path.GetFileName(root);
        Show(new(NodeKind.XmakeWorkspace, title, root, 0, false, _ => XmakeChildren(native, snapshot), "Xmake")
        {
            Tip = root,
            ViewKey = "xmake:root",
        });
    }

    private static List<ExplorerNode> XmakeChildren(XmakeWorkspaceSession native, XmakeWorkspace? snapshot)
    {
        List<ExplorerNode> rows = [];
        if (native.Problem is { } problem)
        {
            bool restricted = problem == XmakeWorkspaceSession.TrustRequired;
            rows.Add(XmakeStatus(restricted ? "Trust required" : "Xmake evaluation failed", problem));
        }
        else if (native.IsEvaluating)
        {
            rows.Add(XmakeStatus(snapshot is null ? "Evaluating Xmake project…" : "Refreshing Xmake project…", null));
        }
        if (snapshot is not null)
        {
            rows.Add(ConfigurationRow(snapshot));
            rows.AddRange(snapshot.Targets.Select(target => TargetRow(snapshot, target)));
            if (snapshot.CompilationDatabaseProblem is { } database)
            {
                rows.Add(XmakeStatus("Compilation database unavailable", database));
            }
        }
        return rows;
    }

    private static ExplorerNode XmakeStatus(string name, string? tip) =>
        new(NodeKind.XmakeStatus, name, null, 1, false, null, tip is null ? null : "Details") { Tip = tip };

    private static ExplorerNode ConfigurationRow(XmakeWorkspace snapshot)
    {
        XmakeConfiguration active = snapshot.Configuration;
        string display = $"{active.Mode} · {active.Platform} · {active.Architecture}";
        string[] names = [.. snapshot.Targets.SelectMany(target => target.Toolchain.Names).Distinct(StringComparer.Ordinal)];
        string? chain = active.Toolchain ?? (names.Length == 1 ? names[0] : names.Length > 1 ? "multiple toolchains" : null);
        return new(NodeKind.XmakeConfiguration, display, null, 1, false,
            _ => [.. names.Select(name => new ExplorerNode(NodeKind.XmakeConfiguration, name, null, 2, false, null, "toolchain"))], chain)
        {
            ViewKey = "xmake:configuration",
            Tip = $"Active Xmake configuration\n{display}\nBuild directory {active.BuildDirectory}\nXmake {snapshot.Installation.BuildVersion}",
        };
    }

    private static ExplorerNode TargetRow(XmakeWorkspace snapshot, XmakeTarget target) =>
        new(NodeKind.XmakeTarget, target.Name, null, 1, false, _ => TargetChildren(snapshot, target), target.Kind)
        {
            NativeTarget = target,
            ViewKey = $"xmake:target:{target.Name}",
            Tip = string.Join('\n', new[] { $"{target.Name} ({target.Kind})", target.Output, target.Script }.OfType<string>()),
        };

    private static List<ExplorerNode> TargetChildren(XmakeWorkspace snapshot, XmakeTarget target)
    {
        List<ExplorerNode> rows = [];
        IEnumerable<(string Name, string Kind)> dependencies = snapshot.Graph.DependenciesOf(target.Name)
            .Select(name => (name, "target")).Concat(target.Packages.Select(p => (p.Name, "package")));
        (string Name, string Kind)[] items = [.. dependencies];
        if (items.Length > 0)
        {
            rows.Add(new(NodeKind.Dependencies, "Dependencies", null, 2, false,
                _ => [.. items.Select(item => new ExplorerNode(NodeKind.XmakeDependency, item.Name, null, 3, false, null, item.Kind))])
            {
                ViewKey = $"xmake:dependencies:{target.Name}",
            });
        }
        rows.AddRange(target.SourceFiles.Concat(target.HeaderFiles).Distinct(PathNames.Comparer)
            .Order(StringComparer.OrdinalIgnoreCase).Select(path => new ExplorerNode(NodeKind.File, Path.GetFileName(path), path, 2, false, null)
            {
                ViewKey = $"xmake:file:{target.Name}:{path}",
                Tip = Path.GetRelativePath(snapshot.Root, path),
            }));
        return rows;
    }

    private IEnumerable<ExplorerNode> FindXmakeFiles(string path, Action<ExplorerNode> through)
    {
        if (Root is not { } root)
        {
            yield break;
        }
        foreach (ExplorerNode row in root.Children ?? [])
        {
            if (row.NativeTarget is not { } target || !target.SourceFiles.Contains(path, PathNames.Comparer)
                && !target.HeaderFiles.Contains(path, PathNames.Comparer))
            {
                continue;
            }
            through(root);
            through(row);
            if (row.Children?.FirstOrDefault(n => n.Path is { } source && PathNames.Same(path, source)) is { } file)
            {
                yield return file;
            }
        }
    }
}

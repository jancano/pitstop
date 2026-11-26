using Passenger.App.Explorer;
using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Text;
using Pitstop.Modules;

namespace Passenger.App;

public partial class MainWindow
{
    private ModuleGraphView? moduleGraph;

    private void AddModules()
    {
        Dock.Add(new Docking.ToolWindow(ToolWindowIds.ModuleGraph, "Module Dependencies", IconKeys.Hierarchy, () =>
        {
            ModuleGraphView graph = new();
            graph.GoTo += (_, path) => NavigateTo(path, default(TextPosition));
            moduleGraph = graph;
            FollowModuleGraph();
            return graph;
        }, Docking.ToolAnchor.BottomLeft) { Serves = ToolWindowServes.XmakeProjects });
        actions!.Add(new IdeAction(AppActions.ShowModules, "Show Modules in Explorer", "Explorer", _ => ShowModules())
        {
            WhyNot = _ => NeedsModuleWorkspace(), Menus = [new MenuPlace("View", 1, 3)],
        });
        actions.Add(new IdeAction(AppActions.ModuleGraph, "Module Dependencies", "C++ Modules", _ => ShowModuleGraph())
        {
            WhyNot = _ => NeedsModuleWorkspace(), Menus = [new MenuPlace("View", 1, 4)],
        });
        actions.Add(new IdeAction(AppActions.GoToModule, "Go to Module", "C++ Modules", _ =>
        {
            if (ModuleAtCaret() is { } provider)
            {
                NavigateTo(provider.Unit.Source, default(TextPosition));
            }
        }) { WhyNot = _ => NeedsModuleWorkspace() ?? MissingProvider(), Menus = [new MenuPlace("Navigate", 2, 8)] });
        actions.Add(new IdeAction(AppActions.RevealModule, "Reveal Module in Explorer", "C++ Modules", _ => ShowModules(ModuleAtCaret()?.Unit.Source))
        { WhyNot = _ => NeedsModuleWorkspace() ?? MissingProvider() });
        actions.Add(new IdeAction(AppActions.ModuleConsumers, "Find Module Consumers", "C++ Modules", _ => ShowModuleGraph(ModuleAtCaret()))
        { WhyNot = _ => NeedsModuleWorkspace() ?? MissingProvider(), Menus = [new MenuPlace("Navigate", 2, 9)] });
        actions.Add(new IdeAction(AppActions.NewModule, "New C++ Module…", "C++ Modules", _ => ShowNewModule())
        { WhyNot = _ => NeedsModuleWorkspace() ?? (session.Xmake?.Snapshot is null ? "Wait for Xmake evaluation." : null), Menus = [new MenuPlace("File", 0, 5)] });

        string? MissingProvider() => ModuleAtCaret() is null ? "Place the caret on a module with a confirmed provider." : null;
    }

    private string? NeedsModuleWorkspace() => session.Xmake is not { IsXmake: true } ? "Open an Xmake workspace."
        : !session.Trusted ? Refusals.Untrusted : null;

    public void ShowModules(string? source = null)
    {
        if (NeedsModuleWorkspace() is null)
        {
            Explorer.ShowView(ExplorerViewKind.Modules);
            explorerView = ExplorerViewKind.Modules;
            KeepWorkspace();
            Dock.Show(ToolWindowIds.Explorer);
            if ((source ?? Editors.Active?.Path) is { } path)
            {
                Explorer.Reveal(path);
            }
        }
    }

    public void ShowModuleGraph(ModuleProvider? focus = null)
    {
        if (NeedsModuleWorkspace() is null)
        {
            Dock.Show(ToolWindowIds.ModuleGraph);
            FollowModuleGraph(focus);
        }
    }

    private void FollowModuleGraph(ModuleProvider? focus = null)
    {
        if (moduleGraph is null)
        {
            return;
        }
        ModuleWorkspace? index = session.Modules?.Snapshot;
        TranslationUnit? document = null;
        if (focus is null && index is not null && Editors.Active is { } active)
        {
            string? target = session.Cpp?.TargetFor(active.Path);
            document = index.UnitsFor(active.Path).Where(unit => target is null || unit.Context.Target == target)
                .OrderBy(unit => unit.Context.Target, StringComparer.Ordinal).FirstOrDefault();
        }
        moduleGraph.Show(index, session.Modules?.Status ?? "Open a trusted Xmake workspace to scan modules.", focus, document);
    }

    public NewModuleDialog? ShowNewModule()
    {
        if (NeedsModuleWorkspace() is not null || session.Xmake is not { Snapshot: { } workspace } native || session.Modules is not { } modules)
        {
            return null;
        }
        ProjectSession current = session;
        NewModuleDialog dialog = new(native, modules, workspace, path =>
        {
            if (session == current && !current.IsEnded)
            {
                OpenFile(path);
            }
        });
        IdeErrors.Observe(dialog.ShowDialog(this));
        return dialog;
    }

    private ModuleProvider? ModuleAtCaret() => session.Modules?.Snapshot is { } snapshot && Editors.Active is { } active
        ? ModuleEditor.ProviderAt(snapshot, active.Path, session.Cpp?.TargetFor(active.Path), active.Text.Snapshot, Editors.CodeEditor.Selections.Primary.Caret)
        : null;
}

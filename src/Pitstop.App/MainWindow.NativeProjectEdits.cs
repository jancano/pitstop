using Pitstop.App.Explorer;
using Pitstop.App.Workspace;
using Pitstop.Core;
using Pitstop.Modules;
using Pitstop.Xmake;

namespace Pitstop.App;

public partial class MainWindow
{
    private void AddNativeProjectActions()
    {
        AddEdit(AppActions.AddNativeTarget, "Add Target…", NativeEditKind.Target);
        AddEdit(AppActions.AddCSource, "Add C Source File…", NativeEditKind.CSource);
        AddEdit(AppActions.AddCppSource, "Add C++ Source File…", NativeEditKind.CppSource);
        AddEdit(AppActions.AddNativeHeader, "Add Header…", NativeEditKind.Header);
        AddEdit(AppActions.NativeTargetDependencies, "Manage Target Dependencies…", NativeEditKind.Dependency);
        AddUnit(AppActions.NewPartition, "Add Module Partition…", ModuleUnitKind.InterfacePartition);
        AddUnit(AppActions.NewModuleImplementation, "Add Module Implementation Unit…", ModuleUnitKind.Implementation);

        void AddEdit(string id, string title, NativeEditKind kind)
        {
            actions!.Add(new IdeAction(id, title, ExplorerGroup, context => ShowNativeProjectEdit(kind, RowOf(context)?.NativeTarget))
            {
                WhyNot = context => NativeEditRefusal(context, needsTarget: kind != NativeEditKind.Target),
            });
        }

        void AddUnit(string id, string title, ModuleUnitKind kind)
        {
            actions!.Add(new IdeAction(id, title, ExplorerGroup, context => ShowNativeModule(kind, RowOf(context)?.NativeTarget))
            {
                WhyNot = context => NativeEditRefusal(context, needsTarget: true),
            });
        }
    }

    private string? NativeEditRefusal(ActionContext context, bool needsTarget) =>
        context.ByAgent ? Refusals.AsksThePerson
        : session.Xmake is not { IsXmake: true } native ? Refusals.NoXmakeProject
        : native.WhyNotOperate ?? (needsTarget && native.Snapshot?.Targets.IsDefaultOrEmpty == true ? "Add an Xmake target first." : null);

    private void ShowNativeProjectEdit(NativeEditKind kind, XmakeTarget? selected)
    {
        if (session.Xmake is not { WhyNotOperate: null, Snapshot: { } workspace } native)
        {
            return;
        }
        ProjectSession current = session;
        NativeProjectEditDialog dialog = new(native, workspace, kind, selected, HasUnsavedScriptChanges, path =>
        {
            if (session == current && !current.IsEnded)
            {
                OpenFile(path);
                Explorer.Reveal(path);
            }
        });
        IdeErrors.Observe(dialog.ShowDialog(this));
    }

    private bool HasUnsavedScriptChanges(string path) => Editors.Find(path)?.IsModified == true;
}

using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Pitstop.App.Explorer;
using Pitstop.Core;
using Pitstop.Modules;
using Pitstop.Xmake;

namespace Pitstop.App.Workspace;

public sealed class NewModuleDialog : IdeWindow
{
    private readonly XmakeWorkspaceSession xmake;
    private readonly ModuleWorkspaceSession modules;
    private readonly XmakeWorkspace workspace;
    private readonly Action<string> open;
    private readonly ComboBox targets = new FormDropDown();
    private readonly ComboBox kinds = new FormDropDown();
    private readonly TextBox name = new() { Name = "ModuleName" };
    private readonly TextBox partition = new() { Name = "ModulePartition" };
    private readonly TextBox path = new() { Name = "ModulePath", PlaceholderText = "Relative to the workspace" };
    private readonly CheckBox standard = new() { Content = "Import the C++23 standard library module" };
    private readonly TextBox preview = new() { AcceptsReturn = true, MinHeight = 105, Name = "ModulePreview" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button confirm = DialogFooter.Default("Create Module");
    private readonly Button openProject = new() { Content = "Open Xmake Script", IsVisible = false };
    private string? created;
    private string? target;
    private bool openCreated = true;
    private XmakeScriptEdit? scriptEdit;
    private XmakeWorkspace? evaluated;

    internal Func<string, bool>? HasUnsavedScriptChanges { get; init; }

    internal NewModuleDialog(XmakeWorkspaceSession xmake, ModuleWorkspaceSession modules, XmakeWorkspace workspace, Action<string> open)
    {
        this.xmake = xmake;
        this.modules = modules;
        this.workspace = workspace;
        this.open = open;
        Title = "New C++ Module";
        Width = DialogWidths.Form;
        Height = 570;
        FormRows.Scope(this);
        targets.ItemsSource = workspace.Targets.Select(item => item.Name).ToArray();
        targets.SelectedIndex = 0;
        kinds.ItemsSource = new[] { ModuleUnitKind.PrimaryInterface, ModuleUnitKind.InterfacePartition, ModuleUnitKind.InternalPartition, ModuleUnitKind.Implementation }
            .Select(kind => new Choice(kind)).ToArray();
        kinds.SelectedIndex = 0;
        kinds.SelectionChanged += (_, _) => UpdatePreview();
        name.TextChanged += (_, _) => UpdatePreview();
        partition.TextChanged += (_, _) => UpdatePreview();
        standard.IsCheckedChanged += (_, _) => UpdatePreview();
        confirm.Click += (_, _) => Create();
        openProject.Click += (_, _) =>
        {
            openCreated = false;
            Close();
            open(workspace.Targets.First(item => item.Name == target).Script);
        };
        xmake.Changed += XmakeChanged;
        Closed += (_, _) =>
        {
            xmake.Changed -= XmakeChanged;
            if (openCreated && created is { } source)
            {
                open(source);
            }
        };
        StackPanel fields = new() { Margin = new Avalonia.Thickness(Gaps.Normal), Spacing = FormRows.Between, Children =
        {
            FormRows.Of("Target", targets), FormRows.Of("Unit kind", kinds), FormRows.Of("Module name", name),
            FormRows.Of("Partition", partition), FormRows.Of("Source path", path), standard,
            FormRows.Of("Preview", preview, top: true), status,
        } };
        Content = DialogFooter.Page(new ScrollViewer { Content = fields }, DialogFooter.OkCancel(confirm, () => Close(), null, openProject));
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (created is not null || kinds.SelectedItem is not Choice choice)
        {
            return;
        }
        partition.IsEnabled = choice.Kind is ModuleUnitKind.InterfacePartition or ModuleUnitKind.InternalPartition;
        string named = name.Text?.Trim() ?? string.Empty;
        string part = partition.Text?.Trim() ?? string.Empty;
        try
        {
            preview.Text = ModuleCreation.Source(named, part, choice.Kind, standard.IsChecked == true);
        }
        catch (ArgumentException)
        {
            preview.Text = string.Empty;
        }
        preview.IsReadOnly = true;
    }

    internal void SelectUnit(ModuleUnitKind kind, string? owner)
    {
        kinds.SelectedItem = kinds.Items.OfType<Choice>().First(choice => choice.Kind == kind);
        if (owner is not null && targets.Items.Contains(owner))
        {
            targets.SelectedItem = owner;
        }
    }

    private void Create()
    {
        if (created is not null)
        {
            ApplyOrClose();
            return;
        }
        try
        {
            WorkspaceTrust.Check(workspace.Root);
            if (!ReferenceEquals(xmake.Snapshot, workspace) || xmake.WhyNotOperate is not null
                || targets.SelectedItem is not string owner || kinds.SelectedItem is not Choice choice)
            {
                status.Text = "The Xmake configuration changed. Reopen New Module to select a current target.";
                return;
            }
            string named = name.Text?.Trim() ?? string.Empty;
            string part = partition.Text?.Trim() ?? string.Empty;
            string source = ModuleCreation.Source(named, part, choice.Kind, standard.IsChecked == true);
            string logical = partition.IsEnabled ? $"{named}:{part}" : named;
            if (modules.Snapshot is not { } index || !ReferenceEquals(index.Workspace, workspace) || index.Problem is not null)
            {
                status.Text = "Wait for module scanning to finish so Pitstop can check names in this configuration.";
                return;
            }
            IReadOnlySet<string> visible = ModuleWorkspace.VisibleTargets(workspace, owner);
            bool exists = index.Named(logical).Any(provider => provider.Unit.Context.Target is { } other && visible.Contains(other));
            if (choice.Kind != ModuleUnitKind.Implementation && exists)
            {
                status.Text = $"{logical} already has a provider visible to {owner}.";
                return;
            }
            if (choice.Kind != ModuleUnitKind.PrimaryInterface && !index.Named(named).Any(provider => provider.Unit.Context.Target == owner && provider.Kind == ModuleUnitKind.PrimaryInterface))
            {
                status.Text = $"Choose a module with a confirmed primary interface in {owner}.";
                return;
            }
            string filename = path.Text?.Trim() ?? string.Empty;
            if (filename.Length == 0)
            {
                string extension = choice.Kind == ModuleUnitKind.Implementation ? ".cpp" : ".cppm";
                filename = logical.Replace(':', '-') + extension;
            }
            string destination = PathNames.Canonical(Path.GetFullPath(filename, workspace.Root));
            if (!PathNames.IsUnder(destination, workspace.Root) || File.Exists(destination) || !Directory.Exists(Path.GetDirectoryName(destination)))
            {
                status.Text = "Choose a new source file in an existing directory inside the workspace.";
                return;
            }
            using (FileStream file = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(Encoding.UTF8.GetBytes(source));
            }
            created = destination;
            target = owner;
            confirm.Content = "Done";
            status.Text = "Source created. Checking its Xmake target membership…";
            xmake.Refresh();
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or UntrustedFolderException)
        {
            status.Text = error.Message;
        }
    }

    private void XmakeChanged(object? sender, EventArgs args)
    {
        if (created is not { } source || target is not { } owner || xmake.IsEvaluating)
        {
            return;
        }
        evaluated = xmake.Snapshot;
        scriptEdit = null;
        if (xmake.Problem is not null || evaluated?.Targets.FirstOrDefault(item => item.Name == owner) is not { } targetInfo)
        {
            status.Text = xmake.Problem ?? "Xmake did not return the selected target. The module file was created.";
            return;
        }
        if (targetInfo.SourceFiles.Contains(source, PathNames.Comparer))
        {
            status.Text = $"Xmake includes the new source in {owner}.";
            return;
        }
        openProject.IsVisible = true;
        try
        {
            scriptEdit = XmakeProjectEdits.AddFile(evaluated!, targetInfo, source, header: false);
            preview.IsReadOnly = false;
            preview.Text = scriptEdit.Addition;
            confirm.Content = "Apply Project Change";
            status.Text = $"Review the text to append to {Path.GetRelativePath(workspace.Root, scriptEdit.Path)}.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or UntrustedFolderException)
        {
            status.Text = error.Message;
        }
    }

    private void ApplyOrClose()
    {
        if (scriptEdit is not { } plan)
        {
            Close();
            return;
        }
        try
        {
            if (!ReferenceEquals(evaluated, xmake.Snapshot) || xmake.WhyNotOperate is not null || HasUnsavedScriptChanges?.Invoke(plan.Path) == true)
            {
                status.Text = "Review the current Xmake configuration and save any unsaved script changes before applying the module.";
                return;
            }
            plan.Apply(preview.Text ?? "");
            xmake.Refresh();
            Close();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or UntrustedFolderException)
        {
            status.Text = error.Message;
        }
    }

    private sealed record Choice(ModuleUnitKind Kind)
    {
        public override string ToString() => ExplorerTree.ModuleLabel(Kind);
    }
}

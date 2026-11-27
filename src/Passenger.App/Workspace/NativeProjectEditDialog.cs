using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Passenger.Core;
using Pitstop.Xmake;

namespace Passenger.App.Workspace;

internal enum NativeEditKind { Target, CSource, CppSource, Header, Dependency }

public sealed class NativeProjectEditDialog : IdeWindow
{
    private readonly XmakeWorkspaceSession session;
    private readonly NativeEditKind kind;
    private readonly Func<string, bool> hasUnsavedChanges;
    private readonly Action<string> open;
    private readonly ComboBox target = new FormDropDown { Name = "NativeEditTarget" };
    private readonly ComboBox choice = new FormDropDown { Name = "NativeEditChoice" };
    private readonly TextBox name = new() { Name = "NativeEditName" };
    private readonly TextBox preview = new() { Name = "NativeEditPreview", AcceptsReturn = true, MinHeight = 160, IsVisible = false,
        Classes = { StyleClasses.Code } };
    private readonly TextBlock status = new() { Name = "NativeEditStatus", TextWrapping = TextWrapping.Wrap };
    private readonly Button submit;
    private XmakeWorkspace workspace;
    private XmakeScriptEdit? plan;
    private string? created;
    private string? owner;
    private bool checkingMembership;
    private bool done;

    internal NativeProjectEditDialog(XmakeWorkspaceSession session, XmakeWorkspace workspace, NativeEditKind kind, XmakeTarget? selected,
        Func<string, bool> hasUnsavedChanges, Action<string> open)
    {
        this.session = session;
        this.workspace = workspace;
        this.kind = kind;
        this.hasUnsavedChanges = hasUnsavedChanges;
        this.open = open;
        Title = kind switch
        {
            NativeEditKind.Target => "Add Target",
            NativeEditKind.CSource => "Add C Source File",
            NativeEditKind.CppSource => "Add C++ Source File",
            NativeEditKind.Header => "Add Header",
            _ => "Manage Target Dependencies",
        };
        Width = DialogWidths.Form;
        Height = 470;
        FormRows.Scope(this);
        StackPanel rows = new() { Margin = new Thickness(Gaps.Normal), Spacing = FormRows.Between };
        if (kind != NativeEditKind.Target)
        {
            target.ItemsSource = workspace.Targets.Select(item => item.Name).ToArray();
            target.SelectedItem = selected?.Name ?? workspace.Targets.FirstOrDefault()?.Name;
            rows.Children.Add(FormRows.Of("Target", target));
        }
        if (kind != NativeEditKind.Dependency)
        {
            name.PlaceholderText = IsSource ? $"Relative to the workspace, e.g. source{Extension}" : null;
            rows.Children.Add(FormRows.Of(IsSource ? "Source path" : "Target name", name));
        }
        if (kind == NativeEditKind.Target)
        {
            choice.ItemsSource = new[] { "binary", "static", "shared", "headeronly" };
            choice.SelectedIndex = 0;
            rows.Children.Add(FormRows.Of("Target kind", choice));
        }
        if (kind == NativeEditKind.Dependency)
        {
            target.SelectionChanged += (_, _) => Dependencies();
            Dependencies();
            rows.Children.Add(FormRows.Of("Add dependency", choice));
        }
        rows.Children.Add(status);
        rows.Children.Add(preview);
        submit = DialogFooter.Default(IsSource ? "Create File" : "Preview Changes");
        submit.Click += (_, _) => Submit();
        name.TextChanged += (_, _) => ClearPreview();
        target.SelectionChanged += (_, _) => ClearPreview();
        choice.SelectionChanged += (_, _) => ClearPreview();
        session.Changed += Evaluated;
        Closed += (_, _) =>
        {
            session.Changed -= Evaluated;
            if (created is { } source)
            {
                open(source);
            }
        };
        Content = DialogFooter.Page(new ScrollViewer { Content = rows }, DialogFooter.OkCancel(submit, Close));
    }

    private bool IsSource => kind is NativeEditKind.CSource or NativeEditKind.CppSource or NativeEditKind.Header;
    private string Extension => kind == NativeEditKind.CSource ? ".c" : kind == NativeEditKind.Header ? ".hpp" : ".cpp";
    private XmakeTarget Target(string? name) => workspace.Targets.FirstOrDefault(item => item.Name == name)
        ?? throw new ArgumentException("Select a target from the evaluated project.");

    private void Dependencies()
    {
        string? selected = target.SelectedItem as string;
        IReadOnlyList<string> current = selected is null ? [] : workspace.Graph.DependenciesOf(selected);
        choice.ItemsSource = workspace.Targets.Where(item => item.Name != selected && !current.Contains(item.Name)).Select(item => item.Name).ToArray();
        choice.SelectedIndex = 0;
        status.Text = current.Count == 0 ? "This target has no evaluated target dependencies."
            : $"Current dependencies: {string.Join(", ", current)}. Remove or change an existing dependency in its Xmake script.";
    }

    private void ClearPreview()
    {
        if (plan is null || created is not null)
        {
            return;
        }
        plan = null;
        preview.IsVisible = false;
        submit.Content = "Preview Changes";
    }

    private string? Refusal() => !ReferenceEquals(workspace, session.Snapshot) ? "The evaluated project changed. Reopen this action to use its current targets."
        : session.WhyNotOperate;

    private void Submit()
    {
        if (done)
        {
            Close();
            return;
        }
        if (Refusal() is { } refused)
        {
            status.Text = refused;
            return;
        }
        try
        {
            WorkspaceTrust.Check(workspace.Root);
            if (plan is { } ready)
            {
                if (hasUnsavedChanges(ready.Path))
                {
                    status.Text = "Save or discard the unsaved editor changes to the Xmake script, then reopen this action.";
                    return;
                }
                ready.Apply(preview.Text ?? "");
                session.Refresh();
                Close();
            }
            else if (IsSource)
            {
                CreateSource();
            }
            else
            {
                ShowPreview(kind == NativeEditKind.Target
                    ? XmakeProjectEdits.AddTarget(workspace, name.Text?.Trim() ?? "", (string?)choice.SelectedItem ?? "")
                    : XmakeProjectEdits.AddDependency(workspace, Target(target.SelectedItem as string), Target(choice.SelectedItem as string)));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or UntrustedFolderException)
        {
            status.Text = error.Message;
        }
    }

    private void CreateSource()
    {
        owner = Target(target.SelectedItem as string).Name;
        string source = XmakeProjectEdits.NewSourcePath(workspace, name.Text?.Trim() ?? "", Extension);
        using (FileStream file = new(source, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            if (kind == NativeEditKind.Header)
            {
                file.Write(Encoding.UTF8.GetBytes("#pragma once\n"));
            }
        }
        created = source;
        checkingMembership = true;
        submit.IsEnabled = false;
        target.IsEnabled = name.IsEnabled = false;
        status.Text = "Checking Xmake target membership…";
        session.Refresh();
    }

    private void Evaluated(object? sender, EventArgs args)
    {
        if (!checkingMembership || session.IsEvaluating || created is not { } source)
        {
            return;
        }
        checkingMembership = false;
        submit.IsEnabled = true;
        if (session.Snapshot is not { } snapshot || session.Problem is not null
            || snapshot.Targets.FirstOrDefault(item => item.Name == owner) is not { } destination)
        {
            Finish(session.Problem ?? "Xmake did not return the selected target. The file was created.");
            return;
        }
        workspace = snapshot;
        bool header = kind == NativeEditKind.Header;
        if ((header ? destination.HeaderFiles : destination.SourceFiles).Contains(source, PathNames.Comparer))
        {
            Finish($"Xmake includes this file in {destination.Name}.");
            return;
        }
        try
        {
            ShowPreview(XmakeProjectEdits.AddFile(snapshot, destination, source, header));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or UntrustedFolderException)
        {
            Finish(error.Message);
        }
    }

    private void Finish(string message)
    {
        status.Text = message;
        submit.Content = "Done";
        done = true;
    }

    private void ShowPreview(XmakeScriptEdit edit)
    {
        plan = edit;
        preview.Text = edit.Addition;
        preview.IsVisible = true;
        status.Text = $"Review the text to append to {Path.GetRelativePath(workspace.Root, edit.Path)}.";
        submit.Content = "Apply Project Change";
    }
}

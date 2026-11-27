using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Pitstop.App;
using Pitstop.App.Explorer;
using Pitstop.App.Workspace;
using Pitstop.Core;
using Pitstop.Xmake;

namespace Pitstop.Tests;

public sealed class XmakeWorkspaceLifecycleTests
{
    [AvaloniaFact]
    public void Native_desktop_ignores_managed_projects_without_losing_generic_editing()
    {
        string folder = NewWorkspace();
        string source = TestFolders.Write(folder, "Program.cs", "class Example {}");
        TestFolders.Write(folder, "App.csproj", TestProjects.Console());
        TestFolders.Write(folder, "App.slnx", "<Solution><Project Path=\"App.csproj\" /></Solution>");
        MainWindow window = new(managedDevelopment: false)
        {
            EvaluateXmake = (root, _, _) => Task.FromResult(Snapshot(root, "native")),
            Autosaves = false,
        };
        window.Show();
        try
        {
            Ui.Await(window.OpenFolder(Path.Combine(folder, "xmake.lua")), "the native desktop project");
            Ui.WaitFor(() => window.Session.Xmake?.Snapshot is not null, "the mixed folder's native target");
            Assert.Null(window.Solution);
            Assert.Empty(window.Projects);
            Assert.Null(window.Session.CSharp);
            Assert.Null(window.Actions[AppActions.OpenSolution]);
            Assert.Null(window.Actions[AppActions.NewSolution]);
            Assert.Null(window.Actions[AppActions.InstallDotnetSdk]);
            Assert.Equal(ExplorerViewKind.Xmake, window.ExplorerView.ViewShown);
            Assert.NotNull(window.Actions[AppActions.OpenNativeProject]);
            Assert.NotNull(window.Actions[AppActions.NewNativeProject]);
            Assert.Equal("Close Project", window.Actions[AppActions.CloseSolution]?.Title);
            Assert.DoesNotContain(window.Actions.All, action => action.Id is AppActions.AttachToProcess or AppActions.RunTests or AppActions.ManageNuGet);
            Assert.DoesNotContain(window.Actions.All, action => action.Id is AppActions.ManageUserSecrets or AppActions.InstallWorkloads
                or AppActions.InstallAndroidDependencies or AppActions.MakeMobileEdits or AppActions.KeepAssembliesWhole);
            Assert.False(window.Perform(AppActions.InstallWorkloads));
            Assert.False(window.Perform(AppActions.AttachToProcess));
            Assert.False(window.DockHost.IsShown(ToolWindowIds.Debug));
            Pitstop.App.Settings.SettingsDialog settings = window.ShowSettings();
            Assert.Contains(Pitstop.App.Settings.SettingsDialog.NativeToolchainsPage, settings.Pages);
            Assert.DoesNotContain(Pitstop.App.Settings.SettingsDialog.HotReloadPage, settings.Pages);
            settings.Close();
            Assert.Null(window.Open(source));
            Assert.Equal(source, window.EditorArea.Active?.Path);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Native_project_edits_preview_and_refresh_evaluated_membership()
    {
        string folder = NewWorkspace();
        string script = Path.Combine(folder, XmakeService.ProjectFile);
        string initial = File.ReadAllText(script);
        string covered = Path.Combine(folder, "covered.cpp");
        MainWindow window = OpenWindow((root, _, _) =>
        {
            XmakeWorkspace snapshot = Snapshot(root, "app");
            XmakeTarget item = snapshot.Targets[0];
            if (File.Exists(covered))
            {
                item = item with { SourceFiles = [covered] };
            }
            return Task.FromResult(snapshot with { Targets = [item] });
        });
        try
        {
            Ui.Await(window.OpenFolder(folder), "the Xmake project to open");
            Ui.WaitFor(() => window.Session.Xmake?.Snapshot is not null, "the evaluated target");
            NativeProjectEditDialog target = Open(AppActions.AddNativeTarget);
            Field(target, "NativeEditName").Text = "fresh";
            Ui.ClickButton(target, "Preview Changes");
            TextBox preview = Field(target, "NativeEditPreview");
            Assert.Contains("fresh", preview.Text!, StringComparison.Ordinal);
            Assert.Equal(initial, File.ReadAllText(script));
            preview.Text += "-- reviewed\n";
            Ui.ClickButton(target, "Apply Project Change");
            Assert.EndsWith("-- reviewed\n", File.ReadAllText(script), StringComparison.Ordinal);
            Ui.WaitFor(() => window.Session.Xmake?.IsEvaluating == false, "refresh after the confirmed script edit");
            string confirmed = File.ReadAllText(script);

            NativeProjectEditDialog source = Open(AppActions.AddCppSource);
            Field(source, "NativeEditName").Text = "covered";
            Ui.ClickButton(source, "Create File");
            Ui.WaitFor(() => source.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Xmake includes this file in app."),
                "the confirmed Xmake membership");
            Assert.True(File.Exists(covered));
            Assert.Equal(confirmed, File.ReadAllText(script));
            Ui.ClickButton(source, "Done");
        }
        finally
        {
            window.Close();
        }

        NativeProjectEditDialog Open(string action)
        {
            Assert.True(window.Perform(action));
            Ui.Settle();
            return Assert.Single(window.OwnedWindows.OfType<NativeProjectEditDialog>());
        }

        static TextBox Field(Window dialog, string name) =>
            Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), text => text.Name == name);
    }

    [AvaloniaFact]
    public void Xmake_recognition_keeps_scripts_behind_workspace_trust()
    {
        string folder = NewWorkspace();
        int calls = 0;
        using IDisposable asking = WorkspaceTrust.Ask(folder);
        MainWindow window = OpenWindow((root, _, _) =>
        {
            Assert.True(WorkspaceTrust.Trusts(root));
            Interlocked.Increment(ref calls);
            return Task.FromResult(Snapshot(root, "Trusted target"));
        });
        try
        {
            Ui.Await(window.OpenFolder(folder), "the Xmake folder to open");
            Ui.WaitFor(() => window.AskingTrust is not null, () => "the workspace trust question");
            Ui.ClickButton(window.AskingTrust!, WorkspaceTrustDialog.Untrusted);
            Assert.Equal(ExplorerViewKind.Xmake, window.ExplorerView.ViewShown);
            Assert.Contains(window.ExplorerView.Tree.Rows, row => row.Name == "Trust required");
            Assert.Equal(0, Volatile.Read(ref calls));
            Ui.ClickButton(window, window.Actions[AppActions.TrustFolder]!.Title);
            Ui.WaitFor(() => window.Session.Xmake?.Snapshot is not null, () => window.Session.Xmake?.Problem ?? "the trusted Xmake targets");
            Assert.Contains(window.ExplorerView.Tree.Rows, row => row.Name == "Trusted target");
            Assert.Equal(1, Volatile.Read(ref calls));
            string outside = Path.Combine(Path.GetDirectoryName(folder)!, "removed-build-artifact");
            HashSet<string> paths = [outside];
            window.Session.FilesChanged(new FolderChanges(paths, true, false, false) { ComeOrGone = paths });
            Ui.Settle();
            Assert.Equal(1, Volatile.Read(ref calls));
            WorkspaceTrust.TakeBack(folder);
            // The test host's default trust is suppressed by the outstanding Ask.
            Ui.WaitFor(() => window.Session.Xmake?.Snapshot is null, () => "Xmake metadata to clear when trust is revoked");
        }
        finally
        {
            window.Close();
            WorkspaceTrust.TakeBack(folder);
        }
    }

    [AvaloniaFact]
    public void Missing_Xmake_keeps_the_workspace_and_file_view_available()
    {
        string folder = NewWorkspace();
        string missing = Path.Combine(folder, "missing", "xmake");
        MainWindow window = OpenWindow(new XmakeService(missing).EvaluateAsync);
        try
        {
            Ui.Await(window.OpenFolder(folder), "the Xmake folder to open");
            Ui.WaitFor(() => window.Session.Xmake?.Problem is not null, () => "the Xmake installation error");
            ExplorerNode error = Assert.Single(window.ExplorerView.Tree.Rows, row => row.Kind == NodeKind.XmakeStatus);
            Assert.Contains(XmakeService.ExecutableVariable, error.Tip, StringComparison.Ordinal);
            window.ExplorerView.ShowView(ExplorerViewKind.Files);
            Assert.Contains(window.ExplorerView.Tree.Rows, row => row.Name == XmakeService.ProjectFile);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_refresh_cancels_the_previous_read_and_switching_to_dotnet_ends_native_work()
    {
        string folder = NewWorkspace();
        string dotnet = PathNames.Canonical(TestFolders.Create("after-xmake-"));
        TestFolders.Write(dotnet, "App.csproj", TestProjects.Console());
        TestFolders.Write(dotnet, "App.slnx", "<Solution><Project Path=\"App.csproj\" /></Solution>");
        TestFolders.Write(dotnet, "Program.cs", "System.Console.WriteLine(42);");
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<XmakeWorkspace> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource closingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource closingCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        MainWindow window = OpenWindow(async (root, _, token) =>
        {
            switch (Interlocked.Increment(ref calls))
            {
                case 1:
                    using (token.Register(() => canceled.TrySetResult()))
                    {
                        firstStarted.SetResult();
                        return await first.Task;
                    }
                case 2:
                    return Snapshot(root, "Current");
                default:
                    using (token.Register(() => closingCanceled.TrySetResult()))
                    {
                        closingStarted.TrySetResult();
                        await closingCanceled.Task;
                        token.ThrowIfCancellationRequested();
                        return Snapshot(root, "Closed");
                    }
            }
        });
        try
        {
            Ui.Await(window.OpenFolder(dotnet), "the initial .NET solution to open");
            Ui.WaitFor(() => window.Projects.Count == 1, () => "the initial .NET project to load");
            Assert.Equal(ExplorerViewKind.Solution, window.ExplorerView.ViewShown);
            Ui.Await(window.OpenFolder(folder), "the Xmake folder to open");
            XmakeWorkspaceSession native = Assert.IsType<XmakeWorkspaceSession>(window.Session.Xmake);
            List<string> observed = [];
            native.Changed += (_, _) =>
            {
                if (native.Snapshot is { } snapshot)
                {
                    observed.Add(snapshot.Targets[0].Name);
                }
            };
            Ui.PumpUntil(() => firstStarted.Task.IsCompleted, "the first Xmake evaluation to start");
            TestFolders.Write(folder, XmakeService.ProjectFile, "set_project('updated')\n");
            Ui.PumpUntil(() => canceled.Task.IsCompleted, "the first evaluation to be canceled after saving xmake.lua");
            first.SetResult(Snapshot(folder, "Obsolete"));
            Ui.WaitFor(() => observed.Contains("Current"), () => "the refreshed Xmake evaluation to land");
            Assert.DoesNotContain("Obsolete", observed);

            TestFolders.Write(folder, XmakeService.ProjectFile, "set_project('updated again')\n");
            Ui.PumpUntil(() => closingStarted.Task.IsCompleted, "an in-flight evaluation before switching workspaces");
            Ui.Await(window.OpenFolder(dotnet), "the .NET solution to open");
            Assert.True(closingCanceled.Task.IsCompleted);
            Ui.WaitFor(() => window.Projects.Count == 1, () => "the .NET project to load");
            Assert.Equal(ExplorerViewKind.Solution, window.ExplorerView.ViewShown);
            Assert.Contains(window.ExplorerView.Tree.Rows, row => row.Kind == NodeKind.Project && row.Name == "App");
            Assert.DoesNotContain("Closed", observed);
        }
        finally
        {
            first.TrySetResult(Snapshot(folder, "Obsolete"));
            window.Close();
        }
    }

    private static MainWindow OpenWindow(Func<string, string, CancellationToken, Task<XmakeWorkspace>> evaluate)
    {
        MainWindow window = new() { StartsLanguageServer = false, Autosaves = false, EvaluateXmake = evaluate };
        window.Show();
        return window;
    }

    private static string NewWorkspace()
    {
        string folder = PathNames.Canonical(TestFolders.Create("xmake-workspace-"));
        TestFolders.Write(folder, XmakeService.ProjectFile, "set_project('probe')\n");
        return folder;
    }

    private static XmakeWorkspace Snapshot(string root, string target)
    {
        XmakeConfiguration configuration = new("probe", "debug", "mingw", "x86_64", Path.Combine(root, "build"),
            Path.Combine(root, "xmake.conf"), "llvm");
        XmakeTarget item = new(target, "binary", Path.Combine(root, XmakeService.ProjectFile), null, [], [], [], new(["llvm"], [], null));
        return new(root, new("xmake", new Version(3, 1, 2), "3.1.2"), configuration, [item],
            new([target], System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableArray<string>>.Empty),
            [], [item.Script], null, null);
    }
}

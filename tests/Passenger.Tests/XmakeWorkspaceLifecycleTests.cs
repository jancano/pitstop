using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Workspace;
using Passenger.Core;
using Pitstop.Xmake;

namespace Passenger.Tests;

public sealed class XmakeWorkspaceLifecycleTests
{
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

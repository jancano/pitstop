using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Pitstop.App;
using Pitstop.App.Explorer;
using Pitstop.Core;
using Pitstop.Tests.Debugging.Helpers;
using Pitstop.Xmake;

namespace Pitstop.Tests;

public sealed class XmakeNativeExplorerTests
{
    [AvaloniaFact]
    public void Evaluates_the_Phase_1_modules_fixture_in_the_Explorer()
    {
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        string? xmake = Environment.GetEnvironmentVariable(XmakeService.ExecutableVariable);
        Assert.SkipWhen(!OperatingSystem.IsWindows() || llvm is null || xmake is null,
            "Set PITSTOP_XMAKE and PITSTOP_TEST_LLVM_MINGW to the Phase 1 installations on Windows x64.");
        string fixture = Path.Combine(Debuggees.Root, "tests", "CppWorkspaces", "modules");
        string folder = PathNames.Canonical(TestFolders.Create("xmake-native-"));
        foreach (string file in Directory.EnumerateFiles(fixture))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }
        string database = Path.Combine(folder, "compile_commands.json");
        const string existing = "The user's own compilation database.";
        File.WriteAllText(database, existing);
        Configure(xmake!, llvm!, folder, "debug");
        string project = Path.Combine(folder, XmakeService.ProjectFile);
        string original = File.ReadAllText(project);
        string marker = Path.Combine(TestFolders.Create("xmake-native-marker-"), "evaluated");
        TestFolders.Write(folder, XmakeService.ProjectFile, original + "\n" + $"""
                on_load(function ()
                    io.writefile([===[{marker}]===], 'evaluated')
                end)
            """);

        using IDisposable asking = WorkspaceTrust.Ask(folder);
        MainWindow window = Ui.OpenFolder(folder, width: 1200, height: 720, languageServer: false, autosaves: false);
        try
        {
            Ui.WaitFor(() => window.AskingTrust is not null, () => "the trust question for the native fixture");
            Ui.ClickButton(window.AskingTrust!, WorkspaceTrustDialog.Untrusted);
            Assert.False(File.Exists(marker));
            Ui.ClickButton(window, window.Actions[AppActions.TrustFolder]!.Title);
            XmakeWorkspace initial = Workspace(window);
            Assert.True(File.Exists(marker));
            Assert.Equal(ExplorerViewKind.Xmake, window.ExplorerView.ViewShown);
            Assert.Equal("pitstop-modules", window.ExplorerView.Tree.Root!.Name);
            Assert.Equal(("debug", "mingw", "x86_64", "llvm"), (initial.Configuration.Mode, initial.Configuration.Platform, initial.Configuration.Architecture, initial.Configuration.Toolchain));
            Assert.Equal("arithmetic", Assert.Single(initial.Graph.DependenciesOf("modules")));
            Assert.EndsWith("libarithmetic.a", initial.Targets.Single(target => target.Name == "arithmetic").Output, StringComparison.Ordinal);
            Assert.EndsWith("modules.exe", initial.Targets.Single(target => target.Name == "modules").Output, StringComparison.Ordinal);
            CheckDatabases(initial, database, existing);

            ExplorerTree tree = window.ExplorerView.Tree;
            Ui.Click(window.ExplorerView.ViewSwitchButton);
            Assert.Equal(ExplorerViewKind.Files, window.ExplorerView.ViewShown);
            Assert.Contains(tree.Rows, row => row.Kind == NodeKind.File && row.Name == XmakeService.ProjectFile);
            Ui.Click(window.ExplorerView.ViewSwitchButton);
            Assert.Equal(ExplorerViewKind.Xmake, window.ExplorerView.ViewShown);
            tree.Expand(Row(tree, NodeKind.XmakeTarget, "arithmetic"));
            tree.Expand(Row(tree, NodeKind.XmakeTarget, "modules"));
            tree.Expand(Row(tree, NodeKind.Dependencies, "Dependencies"));
            Assert.Equal("static", Row(tree, NodeKind.XmakeTarget, "arithmetic").Detail);
            Assert.Equal("binary", Row(tree, NodeKind.XmakeTarget, "modules").Detail);
            Assert.Equal("target", Row(tree, NodeKind.XmakeDependency, "arithmetic").Detail);
            Assert.Equal("llvm", Row(tree, NodeKind.XmakeConfiguration, "debug · mingw · x86_64").Detail);
            Assert.Contains(tree.Rows, row => row.Kind == NodeKind.File && row.Name == "numbers.cppm");
            Ui.Settle();
            Assert.Contains(window.ExplorerView.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "implementation.cpp");
            Capture(window);

            TestFolders.Write(folder, "more.lua", """
                target("workspace-extra")
                    set_kind("static")
                    add_files("extra*.cpp")
                    add_packages("workspace-test-dependency")

                target("workspace-shared")
                    set_kind("static")
                    add_files("extra-one.cpp")
                """);
            TestFolders.Write(folder, "extra-one.cpp", "int extra_one() { return 42; }");
            TestFolders.Write(folder, XmakeService.ProjectFile, original + "\nprint('Project script output')\nincludes('more.lua')\n");
            Ui.WaitFor(() => window.Session.Xmake?.Snapshot?.Targets.Any(target => target.Name == "workspace-extra") == true,
                () => window.Session.Xmake?.Problem ?? "the target from the changed xmake.lua");
            XmakeTarget extra = Workspace(window).Targets.Single(target => target.Name == "workspace-extra");
            Assert.Equal("extra-one.cpp", Path.GetFileName(Assert.Single(extra.SourceFiles)));
            Assert.Equal("workspace-test-dependency", Assert.Single(extra.Packages).Name);
            tree.Expand(Row(tree, NodeKind.XmakeTarget, "workspace-extra"));
            ExplorerNode deps = tree.Rows.Last(row => row.Kind == NodeKind.Dependencies);
            tree.Expand(deps);
            Assert.Equal("package", Row(tree, NodeKind.XmakeDependency, "workspace-test-dependency").Detail);
            Assert.Contains(tree.Rows, row => row.Kind == NodeKind.File && row.Name == "extra-one.cpp");
            tree.Filter([Path.Combine(folder, "extra-one.cpp")]);
            Assert.Equal(2, tree.Rows.Count(row => row.Kind == NodeKind.File && row.Name == "extra-one.cpp"));
            tree.Unfilter();

            TestFolders.Write(folder, "extra-two.cpp", "int extra_two() { return 42; }");
            Ui.WaitFor(() => window.Session.Xmake?.Snapshot?.Targets.Any(target => target.Name == "workspace-extra" && target.SourceFiles.Length == 2) == true,
                () => window.Session.Xmake?.Problem ?? "the changed source glob membership");

            Configure(xmake!, llvm!, folder, "release");
            Ui.WaitFor(() => window.Session.Xmake?.Snapshot?.Configuration.Mode == "release",
                () => window.Session.Xmake?.Problem ?? "the active Xmake configuration change");
            Assert.Equal("llvm", Row(tree, NodeKind.XmakeConfiguration, "release · mingw · x86_64").Detail);

            TestFolders.Write(folder, XmakeService.ProjectFile, "target(\n");
            Ui.WaitFor(() => window.Session.Xmake?.Problem?.Contains("xmake.lua", StringComparison.Ordinal) == true,
                () => "an actionable error after an invalid project edit");
            Assert.Contains(tree.Rows, row => row.Kind == NodeKind.XmakeStatus && row.Name == "Xmake evaluation failed");
            Assert.Equal(existing, File.ReadAllText(database));
        }
        finally
        {
            window.Close();
            WorkspaceTrust.TakeBack(folder);
        }
    }

    private static void Configure(string xmake, string llvm, string root, string mode)
    {
        System.Diagnostics.ProcessStartInfo start = ChildProcesses.Command(xmake,
            ["f", "-m", mode, "-p", "mingw", "--toolchain=llvm", $"--mingw={llvm}", $"--sdk={llvm}", "--require=no", "-y"], root);
        start.Environment["XMAKE_STATS"] = "false";
        start.Environment["XMAKE_COLORTERM"] = "nocolor";
        ChildResult result = Ui.Await(ChildProcesses.RunAsync(start, limit: TimeSpan.FromSeconds(60)), "the Phase 1 toolchain to configure");
        Assert.True(result.Succeeded, result.Output + result.Error);
    }

    private static XmakeWorkspace Workspace(MainWindow window)
    {
        Ui.WaitFor(() => window.Session.Xmake is { IsEvaluating: false }, () => "Xmake evaluation to finish");
        return window.Session.Xmake!.Snapshot
            ?? throw new InvalidOperationException(window.Session.Xmake.Problem ?? "Xmake did not return a snapshot.");
    }

    private static ExplorerNode Row(ExplorerTree tree, NodeKind kind, string name) =>
        Assert.Single(tree.Rows, row => row.Kind == kind && row.Name == name);

    private static void CheckDatabases(XmakeWorkspace workspace, string database, string existing)
    {
        XmakeCompilationDatabases files = workspace.CompilationDatabases
            ?? throw new InvalidOperationException(workspace.CompilationDatabaseProblem);
        Assert.False(PathNames.Within(files.Authoritative, workspace.Root));
        Assert.False(PathNames.Same(files.Authoritative, files.Semantic));
        Assert.Equal(existing, File.ReadAllText(database));
        using JsonDocument authoritative = JsonDocument.Parse(File.ReadAllText(files.Authoritative));
        using JsonDocument semantic = JsonDocument.Parse(File.ReadAllText(files.Semantic));
        Assert.Contains(authoritative.RootElement.EnumerateArray(), IsModuleConsumer);
        Assert.DoesNotContain(semantic.RootElement.EnumerateArray(), IsModuleConsumer);

        static bool IsModuleConsumer(JsonElement row) => row.GetProperty("arguments").EnumerateArray()
            .Any(arg => arg.GetString()!.StartsWith("-fmodule-file=arithmetic=", StringComparison.Ordinal));
    }

    private static void Capture(MainWindow window)
    {
        if (Environment.GetEnvironmentVariable("PITSTOP_TEST_XMAKE_SHOTS") is not { Length: > 0 } output)
        {
            return;
        }
        Directory.CreateDirectory(output);
        ThemeVariant? previous = window.RequestedThemeVariant;
        try
        {
            foreach ((string name, ThemeVariant theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
            {
                window.RequestedThemeVariant = theme;
                Ui.Settle();
                using Avalonia.Media.Imaging.WriteableBitmap frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("The Xmake Explorer did not draw a frame.");
                frame.Save(Path.Combine(output, $"xmake-explorer-{name}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.RequestedThemeVariant = previous;
        }
    }
}

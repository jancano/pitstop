using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Editors;
using Passenger.App.Explorer;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Tests.Debugging.Helpers;
using Passenger.Text;
using Pitstop.Modules;
using Pitstop.Xmake;

namespace Passenger.Tests;

public sealed class ModuleIntelligenceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);

    [AvaloniaFact]
    public void Scans_modules_before_build_and_follows_their_providers_in_the_IDE()
    {
        string? xmake = Environment.GetEnvironmentVariable(XmakeService.ExecutableVariable);
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        Assert.SkipWhen(!OperatingSystem.IsWindows() || xmake is null || !File.Exists(xmake) || llvm is null
            || !File.Exists(Path.Combine(llvm, "bin", "clang-scan-deps.exe")),
            "Requires Windows x64 with PITSTOP_XMAKE set to Xmake 3.1.2 and PITSTOP_TEST_LLVM_MINGW set to LLVM-MinGW 23.1.6.");

        foreach (string standard in (string[])["cxx23", "cxx26"])
        {
            string fixture = Path.Combine(Debuggees.Root, "tests", "CppWorkspaces", "modules");
            string root = PathNames.Canonical(TestFolders.Create("module-intelligence-"));
            foreach (string file in Directory.EnumerateFiles(fixture))
            {
                File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
            }
            string project = TestFolders.Write(root, "xmake.lua", File.ReadAllText(Path.Combine(fixture, "xmake.lua")).Replace("cxx23", standard, StringComparison.Ordinal));
            string userDatabase = TestFolders.Write(root, "compile_commands.json", "[]");
            Configure(xmake!, llvm!, root, "debug");
            MainWindow window = Ui.OpenFolder(root, width: 1350, height: 860, languageServer: false, autosaves: false);
            try
            {
                ModuleWorkspace graph = Scan(window);
                ModuleProvider primary = Assert.Single(graph.Named("arithmetic"));
                ModuleProvider partition = Assert.Single(graph.Named("arithmetic:numbers"));
                Assert.Equal(ModuleUnitKind.PrimaryInterface, primary.Kind);
                Assert.Equal(ModuleUnitKind.InterfacePartition, partition.Kind);
                Assert.Equal(ModuleUnitKind.InternalPartition, Assert.Single(graph.Named("arithmetic:detail")).Kind);
                Assert.DoesNotContain(graph.Providers, provider => Path.GetFileName(provider.Unit.Source) == "implementation.cpp");
                Assert.Contains(graph.Imports(primary.Unit), edge => edge.Provider == partition);
                Assert.Contains(graph.Consumers(primary.Unit), edge => edge.Consumer.Context.Target == "modules" && Path.GetFileName(edge.Consumer.Source) == "main.cpp");
                Assert.False(File.Exists(graph.Workspace.Targets.First(target => target.Name == "modules").Output));
                Assert.Equal("[]", File.ReadAllText(userDatabase));
                window.ShowModules(primary.Unit.Source);
                Assert.Equal(ExplorerViewKind.Modules, window.ExplorerView.ViewShown);
                Assert.Contains(window.ExplorerView.Tree.Rows, row => row.Name == "arithmetic:numbers" && row.Detail == "Interface partition");
                window.ShowModuleGraph(primary);
                Ui.Settle();
                Assert.True(window.DockHost.IsShown(ToolWindowIds.ModuleGraph));

                string consumer = Path.Combine(root, "main.cpp");
                Assert.Null(window.OpenFile(consumer));
                Ui.Settle();
                window.EditorArea.CodeEditor.GoTo(new TextPosition(0, 12));
                Assert.True(window.Perform(AppActions.GoToModule));
                Ui.WaitFor(() => window.EditorArea.Active?.Path == primary.Unit.Source, "the scanner's module provider to open", Limit);
                if (standard == "cxx23")
                {
                    Capture(window, "explorer");
                    window.ShowModuleGraph(primary);
                    Capture(window, "graph");
                    Button partitionNode = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                        button.Content is string words && words.StartsWith("arithmetic:numbers  ·", StringComparison.Ordinal));
                    Ui.Click(partitionNode);
                    Assert.Equal(partition.Unit.Source, window.EditorArea.Active?.Path);
                    window.NavigateTo(primary.Unit.Source, default(TextPosition));
                    OpenDocument active = window.EditorArea.Active!;
                    Ui.WaitFor(() => window.Session.Cpp?.For(active)?.Lsp is not null, "clangd to attach", Limit);
                    int end = active.Text.Snapshot.GetLineStart(1) + "export import :".Length;
                    window.EditorArea.CodeEditor.GoTo(active.Text.Snapshot.GetPosition(end));
                    window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(end, "numbers;".Length, "")]);
                    Ui.Run(window, AppActions.Complete);
                    Ui.WaitFor(() => window.Assist.CompletionLabels.Contains(":numbers"), "relative partition completion", Limit);
                    Assert.DoesNotContain(":detail", window.Assist.CompletionLabels);
                    Capture(window, "completion");

                    NewModuleDialog dialog = Assert.IsType<NewModuleDialog>(window.ShowNewModule());
                    TextBox name = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), field => field.Name == "ModuleName");
                    name.Text = "fixture.added";
                    Ui.Settle();
                    Capture(dialog, "new-module");
                    string original = File.ReadAllText(project);
                    Ui.ClickButton(dialog, "Create Module");
                    Ui.WaitFor(() => File.Exists(Path.Combine(root, "fixture.added.cppm")), "created module source", Limit);
                    Ui.WaitFor(() => dialog.GetVisualDescendants().OfType<TextBox>().Any(field => field.Name == "ModulePreview"
                        && field.Text?.Contains("add_files", StringComparison.Ordinal) == true), "editable Xmake source membership preview", Limit);
                    Assert.Equal(original, File.ReadAllText(project));
                    Assert.Equal("export module fixture.added;\n", File.ReadAllText(Path.Combine(root, "fixture.added.cppm")));
                    dialog.Close();
                    Assert.DoesNotContain("numbers;", active.Text.Snapshot.GetLineText(1), StringComparison.Ordinal);
                }
                ModuleWorkspace beforeEdit = Scan(window);
                TestFolders.Write(root, "numbers.cppm", File.ReadAllText(Path.Combine(root, "numbers.cppm")) + "\nexport int added();\n");
                Ui.WaitFor(() => window.Session.Modules?.Snapshot is { } latest && latest != beforeEdit, "saved module changes to produce a new graph", Limit);
                Assert.Equal("[]", File.ReadAllText(userDatabase));
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Keeps_conditional_imports_and_duplicate_names_in_their_Xmake_configurations()
    {
        string? xmake = Environment.GetEnvironmentVariable(XmakeService.ExecutableVariable);
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        Assert.SkipWhen(!OperatingSystem.IsWindows() || xmake is null || !File.Exists(xmake) || llvm is null
            || !File.Exists(Path.Combine(llvm, "bin", "clang-scan-deps.exe")),
            "Requires Windows x64 with PITSTOP_XMAKE set to Xmake 3.1.2 and PITSTOP_TEST_LLVM_MINGW set to LLVM-MinGW 23.1.6.");
        string fixture = Path.Combine(Debuggees.Root, "tests", "CppWorkspaces", "module-contexts");
        string root = PathNames.Canonical(TestFolders.Create("module-contexts-"));
        foreach (string file in Directory.EnumerateFiles(fixture))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }
        Configure(xmake!, llvm!, root, "debug");
        using IDisposable ask = WorkspaceTrust.Ask(root);
        WorkspaceTrust.Trust(root);
        MainWindow window = Ui.OpenFolder(root, languageServer: false, autosaves: false);
        try
        {
            ModuleWorkspace debug = Scan(window);
            Assert.Equal(2, debug.Named("selection").Count());
            ModuleDependency requirement = Assert.Single(debug.Dependencies, edge => edge.Consumer.Context.Target == "consumer" && edge.Name == "selection");
            Assert.Equal("first", requirement.Provider?.Unit.Context.Target);
            Assert.Equal("different-filename.cppm", Path.GetFileName(requirement.Provider?.Unit.Source));
            Assert.Equal(ModuleResolutionEvidence.XmakeCommand, requirement.Evidence);
            Configure(xmake!, llvm!, root, "release");
            Ui.WaitFor(() => window.Session.Modules?.Snapshot?.Workspace.Configuration.Mode == "release", "the Release scan", Limit);
            ModuleWorkspace release = Scan(window);
            Assert.DoesNotContain(release.Dependencies, edge => edge.Consumer.Context.Target == "consumer" && edge.Name == "selection");
            Assert.NotEqual(debug.Named("selection").First().Identity, release.Named("selection").First().Identity);
            WorkspaceTrust.TakeBack(root);
            Ui.WaitFor(() => window.Session.Modules?.Snapshot is null, "trust revocation to clear module data", Limit);

            string cycle = PathNames.Canonical(TestFolders.Create("module-cycle-"));
            foreach (string file in Directory.EnumerateFiles(Path.Combine(Debuggees.Root, "tests", "CppWorkspaces", "module-cycle")))
            {
                File.Copy(file, Path.Combine(cycle, Path.GetFileName(file)));
            }
            Configure(xmake!, llvm!, cycle, "debug");
            Ui.Await(window.OpenFolder(cycle), "the cyclic workspace to open", Limit);
            Ui.WaitFor(() => window.Session.Modules?.Status.Contains("dependency", StringComparison.OrdinalIgnoreCase) == true
                && window.Session.Modules.Snapshot?.Problem is not null, "the cyclic workspace to publish a scan error", Limit);
            Assert.Empty(window.Session.Modules!.Snapshot!.Providers);
            Assert.Empty(window.Session.Modules.Snapshot.Dependencies);
        }
        finally
        {
            window.Close();
            WorkspaceTrust.TakeBack(root);
        }
    }

    private static ModuleWorkspace Scan(MainWindow window)
    {
        Ui.WaitFor(() => window.Session.Modules?.Snapshot is not null, () => window.Session.Modules?.Status ?? "the module scan", Limit);
        ModuleWorkspace snapshot = window.Session.Modules!.Snapshot!;
        Assert.Null(snapshot.Problem);
        return snapshot;
    }

    private static void Configure(string xmake, string llvm, string root, string mode)
    {
        ProcessStartInfo start = ChildProcesses.Command(xmake, ["f", "-m", mode, "-p", "mingw", "--toolchain=llvm", $"--mingw={llvm}", $"--sdk={llvm}", "--require=no", "-y"], root);
        start.Environment["XMAKE_STATS"] = "false";
        ChildResult configured = Ui.Await(ChildProcesses.RunAsync(start, limit: Limit), "Xmake configuration", Limit);
        Assert.True(configured.Succeeded, configured.Output + configured.Error);
    }

    private static void Capture(Window window, string scene)
    {
        if (Environment.GetEnvironmentVariable("PITSTOP_TEST_MODULE_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        Directory.CreateDirectory(folder);
        ThemeVariant? previous = window.RequestedThemeVariant;
        foreach ((string name, ThemeVariant theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            window.RequestedThemeVariant = theme;
            Ui.Settle();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No module UI frame was rendered.");
            frame.Save(Path.Combine(folder, $"{scene}-{name}.png"), PngBitmapEncoderOptions.Default);
        }
        window.RequestedThemeVariant = previous;
    }
}

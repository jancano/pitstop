using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Pitstop.App;
using Pitstop.App.Explorer;
using Pitstop.App.Run;
using Pitstop.App.ToolWindows;
using Pitstop.Core;
using Pitstop.Tests.Debugging.Helpers;
using Pitstop.Xmake;

namespace Pitstop.Tests;

public sealed class XmakeNativeBuildRunTests
{
    private static readonly TimeSpan NativeLimit = TimeSpan.FromMinutes(2);

    [AvaloniaFact]
    public void Builds_reports_diagnostics_and_runs_an_interactive_Xmake_program()
    {
        (string xmake, string llvm) = Tools();
        string folder = Fixture("native-run");
        Configure(xmake, llvm, folder, "debug");
        string database = TestFolders.Write(folder, "compile_commands.json", "[]");
        MainWindow window = new() { Width = 1320, Height = 810, StartsLanguageServer = false, Autosaves = false, StateFolder = TestFolders.Create("native-build-state-") };
        window.Show();
        window.KeepLayout();
        try
        {
            Ui.Await(window.OpenFolder(folder), "the native workspace to open");
            Workspace(window);
            Assert.DoesNotContain(window.Configurations, config => config.XmakeTarget == "runtime");
            Assert.True(window.SelectConfiguration("Xmake: interactive"));
            Assert.Null(window.Actions.WhyNot(AppActions.Debug, window.Actions.Context()));
            Assert.Contains("Native profiling", window.Actions.WhyNot(AppActions.Profile, window.Actions.Context()), StringComparison.Ordinal);
            BuildOutcome built = BuildSelected(window);
            Assert.True(built.Succeeded, string.Join('\n', built.Output));
            Assert.Contains(built.Output, line => line.Contains("[100%]: build ok", StringComparison.Ordinal));
            Capture(window, "build");

            BuildOutcome compiler = BuildSelected(window, "compile-error");
            Assert.Equal(BuildStatus.Failed, compiler.Status);
            Problem error = Assert.Single(compiler.Problems, problem => problem is { Severity: ProblemSeverity.Error, Path: not null });
            Assert.Equal("compile-error.cpp", Path.GetFileName(error.Path));
            Assert.Equal(0, error.Position.Line);
            Assert.Contains(compiler.Problems, problem => problem.Severity == ProblemSeverity.Message && problem.Message.Contains("instantiation", StringComparison.Ordinal));
            Assert.Equal(1, Assert.Single(error.Related).Position.Line);
            window.ShowToolWindow(ToolWindowIds.Problems);
            ProblemsModel problems = window.BottomPanel.Problems;
            problems.Shown = ProblemsTab.Build;
            Ui.Settle();
            ProblemRow row = Assert.Single(problems.Rows, item => item.Problem == error);
            ProblemsView view = Assert.IsType<ProblemsView>(window.DockHost[ToolWindowIds.Problems].Content);
            TreeList list = Assert.Single(view.GetVisualDescendants().OfType<TreeList>());
            list.SelectedItem = row;
            Ui.ChooseFromMenu(list, "Go to Source");
            Assert.Equal(error.Path, window.EditorArea.Active?.Path);
            Assert.Equal(error.Position, window.EditorArea.CodeEditor.CaretPosition);
            Capture(window, "compiler-error");

            BuildOutcome linker = BuildSelected(window, "link-error");
            Assert.Equal(BuildStatus.Failed, linker.Status);
            Problem unresolved = Assert.Single(linker.Problems, problem => problem.Message.Contains("undefined symbol", StringComparison.Ordinal));
            Assert.Null(unresolved.Path);
            Assert.Contains("missing_definition", unresolved.Message, StringComparison.Ordinal);

            Assert.True(window.SelectConfiguration("Xmake: interactive"));
            RunConfigurationsDialog dialog = window.ShowRunConfigurations();
            Ui.Settle();
            Assert.Equal("interactive", dialog.XmakeTargetBox.SelectedItem);
            Ui.Click(dialog.CopyButton);
            const string arguments = "\"space inside\" \"say \\\"hi\\\"\" \"café λ\" \"\" --flag";
            dialog.ArgumentsBox.Text = arguments;
            dialog.WorkingDirectoryBox.Text = ".";
            Ui.Click(dialog.AddVariableButton);
            TextBox name = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), box => box.PlaceholderText == "Name");
            TextBox value = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), box => box.PlaceholderText == "Value");
            name.Text = "FIXTURE_USER";
            value.Text = "custom";
            Ui.Settle();
            Assert.True(dialog.Applied());
            RunConfiguration custom = Assert.IsType<RunConfiguration>(window.Configuration);
            Assert.False(custom.Generated);
            Assert.Equal(arguments, custom.Arguments);
            Assert.Equal("custom", custom.Environment["FIXTURE_USER"]);
            Capture(dialog, "configuration");
            dialog.Close();
            Ui.Launched(window, window.RunAsync(), "Xmake interactive run", NativeLimit);
            WaitForOutput(window, "ready");
            string output = Output(window);
            foreach (string expected in (string[])["before_run", "runtime=42", "FIXTURE_RUNTIME=configured", "FIXTURE_USER=custom", "fixture stderr",
                "arg[1]=space inside", "arg[2]=say \"hi\"", "arg[3]=café λ", "arg[4]=\n", "arg[5]=--flag", $"cwd={folder}"])
            {
                Assert.True(output.Contains(expected, StringComparison.OrdinalIgnoreCase), $"Missing {expected} in:\n{output}");
            }
            Send(window, "hello from Pitstop");
            WaitForOutput(window, "echo=hello from Pitstop");
            Capture(window, "run");
            Send(window, "spawn");
            int child = ChildId(window);
            Task<int> before = window.RunEnd;
            Ui.Launched(window, window.Builds.RerunAsync(), "Xmake Rerun", NativeLimit);
            Assert.True(before.IsCompleted);
            Ui.WaitFor(() => !Processes.IsRunning(child), "Rerun to end the previous run's child");
            WaitForOutput(window, "ready");
            Send(window, "spawn");
            child = ChildId(window);
            window.Perform(AppActions.Stop);
            Ui.PumpUntil(() => window.RunEnd.IsCompleted && !Processes.IsRunning(child), "Stop to end Xmake and its child", NativeLimit);
            Ui.Launched(window, window.RunAsync(), "an Xmake run after Stop", NativeLimit);
            WaitForOutput(window, "ready");
            Send(window, "quit");
            Assert.Equal(0, Ui.Await(window.RunEnd, "interactive run to exit"));
            Assert.Contains("after_run", Output(window), StringComparison.Ordinal);
            Assert.Equal("[]", File.ReadAllText(database));

            Ui.Launched(window, window.RunAsync(), "a native run that exits with an error", NativeLimit);
            WaitForOutput(window, "ready");
            Send(window, "fail");
            Assert.NotEqual(0, Ui.Await(window.RunEnd, "Xmake to report the nonzero native exit"));
            Assert.Contains("failed(7)", Output(window), StringComparison.Ordinal);

            window.SetConfigurations([custom with { Console = ConsoleKind.Terminal, BuildBeforeLaunch = false }], custom.Name);
            Ui.Launched(window, window.RunAsync(), "Xmake integrated terminal launch", NativeLimit);
            TerminalRun terminal = Assert.IsType<TerminalRun>(window.RunInTerminal);
            Ui.WaitFor(() => TerminalText().Contains("ready", StringComparison.Ordinal), "the native program's terminal output", NativeLimit);
            string terminalOutput = TerminalText();
            Assert.True(terminalOutput.Contains("arg[3]=café λ", StringComparison.Ordinal), terminalOutput);
            Assert.Contains("arg[4]=\n", terminalOutput, StringComparison.Ordinal);
            Assert.Contains("runtime=42", terminalOutput, StringComparison.Ordinal);
            string other = TestFolders.Create("native-run-switch-");
            Ui.Await(window.OpenFolder(other), "another folder to open");
            Assert.True(terminal.Ended.IsCompleted);
            Ui.Await(window.OpenFolder(folder), "the native fixture to reopen");
            Workspace(window);
            Assert.Equal(custom.Name, window.Configuration?.Name);
            Assert.Equal(custom.Arguments, window.Configuration?.Arguments);
            Assert.Equal(ConsoleKind.Terminal, window.Configuration?.Console);
            Assert.Equal("custom", window.Configuration?.Environment["FIXTURE_USER"]);

            string TerminalText() => string.Concat(terminal.Session.Screen.Scrollback.Concat(terminal.Session.Screen.Lines)
                .Select(line => line.Text() + (line.Wrapped ? string.Empty : "\n")));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Defers_evaluation_during_an_Xmake_build_and_stops_it_when_trust_is_revoked()
    {
        (string xmake, string llvm) = Tools();
        string folder = Fixture("native-run");
        string control = TestFolders.Create("xmake-build-control-");
        string marker = Path.Combine(control, "began");
        string release = Path.Combine(control, "release");
        string script = Path.Combine(folder, "xmake.lua");
        File.AppendAllText(script, $$"""

            target("waiting")
                set_kind("phony")
                set_default(false)
                before_build(function ()
                    io.writefile([===[{{marker}}]===], "started")
                    while not os.isfile([===[{{release}}]===]) do os.sleep(25) end
                end)
            """);
        Configure(xmake, llvm, folder, "debug");
        using IDisposable asking = WorkspaceTrust.Ask(folder);
        WorkspaceTrust.Trust(folder);
        MainWindow window = Ui.OpenFolder(folder, languageServer: false, autosaves: false);
        try
        {
            XmakeWorkspace initial = Workspace(window);
            Task<BuildOutcome> first = StartBuild();
            Ui.PumpUntil(() => File.Exists(marker), "the before_build hook to start", NativeLimit);
            File.AppendAllText(script, "\n-- configuration input changed while building\n");
            Ui.PumpUntil(() => window.Session.Xmake!.IsEvaluating, "a pending Xmake refresh", NativeLimit);
            Assert.Same(initial, window.Session.Xmake!.Snapshot);
            Assert.True(window.IsBuilding);
            File.WriteAllText(release, string.Empty);
            BuildOutcome obsolete = Ui.Await(first, "the obsolete Xmake build to finish", NativeLimit);
            Assert.Equal(BuildStatus.Stopped, obsolete.Status);
            Assert.Empty(obsolete.Problems);
            Assert.NotSame(initial, Workspace(window));

            File.Delete(marker);
            File.Delete(release);
            Task<BuildOutcome> second = StartBuild();
            Ui.PumpUntil(() => File.Exists(marker), "a second Xmake build to start", NativeLimit);
            Assert.True(window.Perform(AppActions.Stop));
            Assert.Equal(BuildStatus.Stopped, Ui.Await(second, "the canceled build to finish", NativeLimit).Status);

            File.Delete(marker);
            Task<BuildOutcome> third = StartBuild();
            Ui.PumpUntil(() => File.Exists(marker), "the Xmake build to start before trust revocation", NativeLimit);
            OwnProcess process = OwnProcess.Of(window.Session.Builder.ProcessId ?? 0);
            WorkspaceTrust.TakeBack(folder);
            Assert.Equal(BuildStatus.Stopped, Ui.Await(third, "trust revocation to end the build", NativeLimit).Status);
            Assert.False(Processes.IsRunning(process.Id));
            Assert.Null(window.Session.Xmake!.Snapshot);
            Assert.False(window.Perform(AppActions.Build));
        }
        finally
        {
            File.WriteAllText(release, string.Empty);
            window.Close();
            WorkspaceTrust.TakeBack(folder);
        }

        Task<BuildOutcome> StartBuild()
        {
            TaskCompletionSource<BuildOutcome> outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
            window.ExplorerView.Choose(Assert.Single(window.ExplorerView.Tree.Rows, row => row.Kind == NodeKind.XmakeTarget && row.Name == "waiting"));
            void Finished(object? sender, BuildOutcome result)
            {
                window.Builds.BuildFinished -= Finished;
                outcome.TrySetResult(result);
            }
            window.Builds.BuildFinished += Finished;
            Assert.True(window.Perform(AppActions.BuildSelected));
            return outcome.Task;
        }
    }

    [AvaloniaFact]
    public void Builds_and_runs_named_modules_and_import_std_in_Cpp23_and_Cpp26()
    {
        (string xmake, string llvm) = Tools();
        foreach (string fixture in (string[])["modules", "standard-library"])
        {
            foreach (string standard in (string[])["cxx23", "cxx26"])
            {
                string folder = Fixture(fixture);
                string script = Path.Combine(folder, "xmake.lua");
                File.WriteAllText(script, File.ReadAllText(script).Replace("cxx23", standard, StringComparison.Ordinal));
                Configure(xmake, llvm, folder, "debug");
                MainWindow window = Ui.OpenFolder(folder, languageServer: false, autosaves: false);
                try
                {
                    XmakeWorkspace snapshot = Workspace(window);
                    string output = Assert.Single(snapshot.Targets, target => target.Kind == "binary").Output!;
                    BuildOutcome first = Ui.Await(window.BuildAsync(), "all Xmake targets to build", NativeLimit);
                    Assert.True(first.Succeeded, string.Join('\n', first.Output));
                    DateTime written = File.GetLastWriteTimeUtc(output);
                    BuildOutcome incremental = Ui.Await(window.BuildAsync(), "an unchanged build", NativeLimit);
                    Assert.True(incremental.Succeeded, string.Join('\n', incremental.Output));
                    Assert.Equal(written, File.GetLastWriteTimeUtc(output));
                    RunOk(window);
                    if (fixture == "modules")
                    {
                        TestFolders.Write(folder, "implementation.cpp", "module arithmetic;\nimport :detail;\nint answer() { return base_number() + increment() + 0; }\n");
                        BuildOutcome implementation = BuildSelected(window);
                        Assert.True(implementation.Succeeded, string.Join('\n', implementation.Output));
                        Assert.Contains(implementation.Output, line => line.Contains("implementation.cpp", StringComparison.Ordinal));
                        TestFolders.Write(folder, "arithmetic.cppm", File.ReadAllText(Path.Combine(folder, "arithmetic.cppm")) + "\nexport constexpr int new_export() { return 42; }\n");
                        TestFolders.Write(folder, "main.cpp", "import arithmetic;\nint main() { return new_export() == answer() ? 0 : 1; }\n");
                        RunOk(window);
                    }
                    Assert.True(BuildAction(window, AppActions.CleanSelected).Succeeded, BuildText(window));
                    Assert.False(File.Exists(output));
                    Assert.True(BuildAction(window, AppActions.RebuildSelected).Succeeded, BuildText(window));
                    Assert.True(File.Exists(output), BuildText(window));
                    Assert.True(BuildAction(window, AppActions.CleanSolution).Succeeded, BuildText(window));
                    Assert.False(File.Exists(output));
                    Assert.True(File.Exists(snapshot.Configuration.ConfigurationFile));
                    Configure(xmake, llvm, folder, "release");
                    Ui.WaitFor(() => window.Session.Xmake?.Snapshot?.Configuration.Mode == "release", "the Release configuration to become active", NativeLimit);
                    RunOk(window);
                    Assert.Contains("release, mingw", BuildText(window), StringComparison.Ordinal);
                }
                finally
                {
                    window.Close();
                }
            }
        }
    }

    private static BuildOutcome BuildSelected(MainWindow window, string? target = null)
    {
        if (target is not null)
        {
            window.ExplorerView.Choose(Assert.Single(window.ExplorerView.Tree.Rows, row => row.Kind == NodeKind.XmakeTarget && row.Name == target));
            Assert.True(window.SelectConfiguration($"Xmake: {target}"));
        }
        return BuildAction(window, AppActions.BuildSelected);
    }

    private static BuildOutcome BuildAction(MainWindow window, string action)
    {
        Workspace(window);
        BuildOutcome? outcome = null;
        void Finished(object? sender, BuildOutcome result) => outcome = result;
        window.Builds.BuildFinished += Finished;
        try
        {
            Assert.True(window.Perform(action), Ui.Said(window));
            Ui.PumpUntil(() => outcome is not null, $"{action} to finish", NativeLimit);
            return outcome!;
        }
        finally
        {
            window.Builds.BuildFinished -= Finished;
        }
    }

    private static (string Xmake, string Llvm) Tools()
    {
        string? xmake = Environment.GetEnvironmentVariable(XmakeService.ExecutableVariable);
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        Assert.SkipWhen(!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64
            || xmake is null || !File.Exists(xmake) || llvm is null || !File.Exists(Path.Combine(llvm, "bin", "clang.exe")),
            "Native Build and Run requires Windows x64, PITSTOP_XMAKE pointing to Xmake 3.1.2, and PITSTOP_TEST_LLVM_MINGW pointing to LLVM-MinGW 23.1.6.");
        return (xmake!, llvm!);
    }

    private static string Fixture(string name)
    {
        string folder = PathNames.Canonical(TestFolders.Create("native-build-run-"));
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Debuggees.Root, "tests", "CppWorkspaces", name)))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }
        return folder;
    }

    private static void Configure(string xmake, string llvm, string folder, string mode)
    {
        ProcessStartInfo start = ChildProcesses.Command(xmake,
            ["f", "-m", mode, "-p", "mingw", "--toolchain=llvm", $"--mingw={llvm}", $"--sdk={llvm}", "--require=no", "-y"], folder);
        start.Environment["XMAKE_STATS"] = "false";
        start.Environment["XMAKE_COLORTERM"] = "nocolor";
        ChildResult configured = Ui.Await(ChildProcesses.RunAsync(start, limit: NativeLimit), $"Xmake to configure {mode}", NativeLimit);
        Assert.True(configured.Succeeded, configured.Output + configured.Error);
    }

    private static XmakeWorkspace Workspace(MainWindow window)
    {
        Ui.WaitFor(() => window.Session.Xmake is { IsEvaluating: false }, "Xmake workspace evaluation", NativeLimit);
        return window.Session.Xmake!.Snapshot ?? throw new InvalidOperationException(window.Session.Xmake.Problem);
    }

    private static string Output(MainWindow window) => window.BottomPanel.Run.Document.Snapshot.ToString();
    private static string BuildText(MainWindow window) => window.BottomPanel.Build.Document.Snapshot.ToString();
    private static void WaitForOutput(MainWindow window, string text) =>
        Ui.WaitFor(() => Output(window).Contains(text, StringComparison.Ordinal), () => $"{text} in the Run console\n{Output(window)}", NativeLimit);

    private static void Send(MainWindow window, string line)
    {
        Ui.Click(window.BottomPanel.Run.Editor, settle: false);
        window.KeyTextInput(line);
        Ui.Press(window, "ENTER");
    }

    private static int ChildId(MainWindow window)
    {
        WaitForOutput(window, "child=");
        string line = Output(window).Split('\n').Last(text => text.StartsWith("child=", StringComparison.Ordinal));
        Assert.True(int.TryParse(line.AsSpan(6), out int child) && Processes.IsRunning(child), $"The fixture did not start a child: {line}");
        return child;
    }

    private static void RunOk(MainWindow window)
    {
        Ui.Launched(window, window.RunAsync(), "the native program to start", NativeLimit);
        Assert.Equal(0, Ui.Await(window.RunEnd, "the native program's exit", NativeLimit));
        Ui.Settle();
    }

    private static void Capture(Window window, string state)
    {
        if (Environment.GetEnvironmentVariable("PITSTOP_TEST_NATIVE_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        Directory.CreateDirectory(folder);
        ThemeVariant? previous = window.RequestedThemeVariant;
        foreach ((string name, ThemeVariant theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            window.RequestedThemeVariant = theme;
            Ui.Settle();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No rendered frame for {state}.");
            frame.Save(Path.Combine(folder, $"{state}-{name}.png"), PngBitmapEncoderOptions.Default);
        }
        window.RequestedThemeVariant = previous;
    }
}

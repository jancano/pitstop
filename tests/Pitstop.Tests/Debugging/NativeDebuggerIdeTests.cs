using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Pitstop.App;
using Pitstop.App.Debugging;
using Pitstop.App.Run;
using Pitstop.App.Settings;
using Pitstop.App.ToolWindows;
using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.NativeDebug;
using Pitstop.Tests.Debugging.Helpers;
using Pitstop.Xmake;

namespace Pitstop.Tests.Debugging;

public sealed class NativeDebuggerIdeTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [AvaloniaFact]
    public void Native_breakpoint_settings_and_watches_round_trip_without_an_adapter()
    {
        BreakpointStore store = new();
        string source = Path.Combine(TestFolders.Create("debug-settings-"), "main.cpp");
        LineBreakpoint point = store.Set(source, 6);
        store.SetCondition(point, "count == 4", "2");
        store.AddNativeFunction("parse");
        store.Restore(store.Capture() with { NativeWatches = ["count", "count + 1"] });
        BreakpointsState saved = store.Capture();
        Assert.Collection(saved.NativeWatches, watch => Assert.Equal("count", watch), watch => Assert.Equal("count + 1", watch));
        Assert.Equal("parse", Assert.Single(saved.NativeFunctions).Name);
        LineBreakpoint restored = Assert.Single(store.In(source));
        Assert.Equal(6, restored.Line);
        Assert.Equal("count == 4", restored.Condition);
        Assert.Equal("2", restored.HitCondition);
    }

    [AvaloniaFact]
    public void F5_builds_a_native_target_and_opens_real_LLDB_frames_and_variables()
    {
        string root = Configure("native-debug");
        string unused = TestFolders.Write(root, "unused.hpp", "inline int never_linked() { return -1; }");
        MainWindow window = Open(root);
        try
        {
            Assert.True(window.SelectConfiguration("Xmake: debug-fixture"));
            string path = Path.Combine(root, "main.cpp");
            Assert.Null(window.OpenFile(path));
            int line = Mark(path, "locals-stop");
            Ui.PlaceAfter(window.EditorArea.CodeEditor, "// locals-stop");
            Assert.True(window.Perform(AppActions.ToggleBreakpoint));
            LineBreakpoint point = Assert.Single(window.Session.Debug.Breakpoints.In(path));
            Assert.Equal(line, point.Line);
            window.Session.Debug.Breakpoints.SetCondition(point, "seed == 11", null);
            LineBreakpoint pending = window.Session.Debug.Breakpoints.Set(unused, 0);
            long launch = Stopwatch.GetTimestamp();
            Ui.Press(window, "F5");
            WaitForStop(window, path);
            TimeSpan firstStop = Stopwatch.GetElapsedTime(launch);
            Assert.Equal(BreakpointStatus.Bound, point.Status);
            Assert.NotEqual(BreakpointStatus.Bound, pending.Status);
            Assert.Equal(line, point.Line);
            Assert.Contains(window.BottomPanel.Debug.Frames, frame => frame.Name.Contains("inspect", StringComparison.Ordinal));
            Assert.NotEmpty(window.BottomPanel.Debug.Threads);
            Ui.WaitFor(() => window.BottomPanel.Debug.Tree.Rows.Any(row => row is { Name: "seed", Value: "11" }),
                "LLDB function argument in Variables", Limit);
            StopTimes? locals = window.Session.Debug.LastStopTimes;
            DapStackFrame caller = Assert.Single(window.BottomPanel.Debug.Frames,
                frame => frame.Name.TrimStart(':').StartsWith("wmain(", StringComparison.Ordinal));
            ListBox stack = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => ReferenceEquals(list.ItemsSource, window.BottomPanel.Debug.Frames));
            long selecting = Stopwatch.GetTimestamp();
            stack.SelectedItem = caller;
            Ui.WaitFor(() => window.BottomPanel.Debug.Tree.Rows.All(row => row.Name != "seed") && window.EditorArea.Active?.Path == path,
                "caller frame inspection", Limit);
            stack.SelectedItem = window.BottomPanel.Debug.Frames[0];
            Ui.WaitFor(() => window.BottomPanel.Debug.Tree.Rows.Any(row => row is { Name: "seed", Value: "11" }), "original frame inspection", Limit);
            TimeSpan frameSelection = Stopwatch.GetElapsedTime(selecting);
            VariableNode vector = Assert.Single(window.BottomPanel.Debug.Tree.Rows, row => row.Name == "items");
            long expanding = Stopwatch.GetTimestamp();
            Ui.Await(window.BottomPanel.Debug.Tree.Expand(vector), "libc++ synthetic children", Limit);
            TimeSpan expansion = Stopwatch.GetElapsedTime(expanding);
            Assert.Contains(window.BottomPanel.Debug.Tree.Rows, row => row is { Name: "[0]", Value: "2" });
            window.BottomPanel.Debug.FocusEvaluate("seed");
            Ui.Press(window, "CTRL+SHIFT+ENTER");
            Ui.WaitFor(() => window.BottomPanel.Debug.Tree.Rows.Any(row => row is { Role: VariableRole.Watch, Name: "seed", Value: "11" }),
                "a watch uses the actual frame value", TimeSpan.FromSeconds(25));
            window.BottomPanel.Debug.FocusEvaluate("items");
            Ui.Press(window, "CTRL+SHIFT+ENTER");
            Ui.WaitFor(() => window.BottomPanel.Debug.Tree.Rows.Any(row => row is { Role: VariableRole.Watch, Name: "items" }),
                "the vector watch uses a debugger-supplied reference", TimeSpan.FromSeconds(25));
            VariableNode watchedVector = Assert.Single(window.BottomPanel.Debug.Tree.Rows,
                row => row is { Role: VariableRole.Watch, Name: "items" });
            Ui.Await(window.BottomPanel.Debug.Tree.Expand(watchedVector), "the expanded vector watch in Variables", Limit);
            Capture(window, "native-debug-variables");
            VariableNode large = Assert.Single(window.BottomPanel.Debug.Tree.Rows, row => row.Name == "large");
            long expandingLarge = Stopwatch.GetTimestamp();
            Ui.Await(window.BottomPanel.Debug.Tree.Expand(large), "the first page of the large vector", Limit);
            VariableNode page = Assert.Single(window.BottomPanel.Debug.Tree.Rows, row => row.Name == "Next 100 values");
            Ui.Await(window.BottomPanel.Debug.Tree.Expand(page), "the second page of the large vector", Limit);
            Assert.Contains(window.BottomPanel.Debug.Tree.Rows, row => row is { Name: "[100]", Value: "7" });
            TimeSpan largeExpansion = Stopwatch.GetElapsedTime(expandingLarge);
            Ui.Await(window.BottomPanel.Debug.Tree.Collapse(large), "collapse the paged vector", Limit);
            window.BottomPanel.Debug.FocusEvaluate("seed + 2");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => window.BottomPanel.Debug.Tree.Rows.Any(row => row is { Role: VariableRole.Result, Value: "13" }),
                "an explicit LLDB expression result", Limit);
            window.BottomPanel.Debug.ShowTab(console: true);
            TextBox console = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Native debugger console expression");
            console.Text = "seed + 3";
            console.Focus();
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => window.Session.Debug.Output().ToString().Contains("Expression result: 14", StringComparison.Ordinal),
                "the explicit LLDB console result", Limit);
            Capture(window, "native-debug-expression-console");
            window.BottomPanel.Debug.ShowTab(console: false);
            window.ShowBreakpoints(point);
            Capture(window, "native-debug-breakpoints");
            Ui.Press(window, "ESCAPE");
            int stopped = window.Session.Debug.Session!.Stops;
            long stepping = Stopwatch.GetTimestamp();
            Assert.True(window.Perform(AppActions.StepOver));
            Ui.WaitFor(() => window.Session.Debug.Session is { State: DebugState.Paused } session && session.Stops > stopped, "an LLDB step", Limit);
            TimeSpan step = Stopwatch.GetElapsedTime(stepping);
            Ui.PlaceAfter(window.EditorArea.CodeEditor, "// shared-call");
            Assert.True(window.Perform(AppActions.RunToCursor));
            Ui.WaitFor(() => window.Session.Debug.Stop?.Top?.Line == Mark(path, "shared-call") + 1 &&
                window.Session.Debug.Session?.State == DebugState.Paused, "LLDB Run to Cursor reaches the temporary breakpoint", Limit);
            long stopping = Stopwatch.GetTimestamp();
            Assert.True(window.Perform(AppActions.Stop));
            Ui.WaitFor(() => !window.Session.Debug.IsDebugging, "native debug shutdown", Limit);
            TimeSpan shutdown = Stopwatch.GetElapsedTime(stopping);
            Assert.Empty(window.BottomPanel.Debug.Tree.Rows);
            if (Environment.GetEnvironmentVariable("PITSTOP_TEST_DEBUG_SHOTS") is { Length: > 0 } destination)
            {
                File.WriteAllText(Path.Combine(destination, "native-debug-timings.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    firstStopMilliseconds = firstStop.TotalMilliseconds,
                    locals,
                    twoFrameSelectionsMilliseconds = frameSelection.TotalMilliseconds,
                    smallVectorExpansionMilliseconds = expansion.TotalMilliseconds,
                    pagedVectorExpansionMilliseconds = largeExpansion.TotalMilliseconds,
                    stepMilliseconds = step.TotalMilliseconds,
                    shutdownMilliseconds = shutdown.TotalMilliseconds,
                }));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("cxx23")]
    [InlineData("cxx26")]
    public void Module_partitions_break_and_step_in_the_IDE(string standard)
    {
        string root = Configure("native-debug-modules", standard == "cxx26" ? ["--cpp26=y"] : []);
        string database = TestFolders.Write(root, "compile_commands.json", "user compilation database");
        MainWindow window = Open(root);
        try
        {
            RunConfiguration configuration = new()
            {
                Name = "Module debug", Kind = RunKind.Xmake, XmakeTarget = "debug-modules", NativeStopOnEntry = true,
            };
            window.SetConfigurations([configuration], configuration.Name);
            string path = Path.Combine(root, "partition.cppm");
            Assert.Null(window.OpenFile(path));
            window.Session.Debug.Breakpoints.Set(path, Mark(path, "partition-stop"));
            string main = Path.Combine(root, "main.cpp");
            Assert.Null(window.OpenFile(main));
            window.Session.Debug.Breakpoints.Set(main, Mark(main, "consumer-stop"));
            Ui.WaitFor(() => window.Session.Modules?.Snapshot?.Named("debug_values:partition").Any() == true,
                "module partition indexing before the debugger starts", Limit);
            Ui.Launched(window, window.DebugAsync(), "the native module debug session", Limit);
            Ui.WaitFor(() => window.Session.Debug.Session is { State: DebugState.Paused, StopReason: "entry" },
                "the configured LLDB entry stop", Limit);
            Assert.True(window.Perform(AppActions.Resume));
            WaitForStop(window, main);
            Assert.True(window.Perform(AppActions.StepInto));
            WaitForStop(window, Path.Combine(root, "facade.cppm"));
            Assert.True(window.Perform(AppActions.Resume));
            WaitForStop(window, path);
            Assert.Equal(path, window.EditorArea.Active?.Path, PathNames.Comparer);
            Assert.Equal(BreakpointStatus.Bound, Assert.Single(window.Session.Debug.Breakpoints.In(path)).Status);
            if (standard == "cxx23")
            {
                Assert.Contains(window.Session.Debug.Session!.ModuleContext(path)!.Providers, provider => provider.Name == "debug_values:partition");
                Capture(window, "native-debug-partition");
            }
            Assert.True(window.Perform(AppActions.StepOut));
            WaitForStop(window, Path.Combine(root, "implementation.cpp"));
            Assert.True(window.Perform(AppActions.Resume));
            Ui.WaitFor(() => window.Session.Debug.Session?.State == DebugState.Ended, "the module debuggee to exit", Limit);
            Assert.Equal(0, window.Session.Debug.Session!.ExitCode);
            Assert.Equal("user compilation database", File.ReadAllText(database));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Source_mapping_navigates_to_the_real_file_from_a_build_directory_that_does_not_exist()
    {
        string root = Configure("native-debug");
        const string remote = "C:/pitstop-virtual-debug-source";
        File.AppendAllText(Path.Combine(root, "xmake.lua"), $$"""

            target("debug-fixture")
                add_cxxflags("-fdebug-compilation-dir={{remote}}", {force = true})
            """);
        MainWindow window = Open(root);
        try
        {
            RunConfiguration mapped = new()
            {
                Name = "Mapped debug", Kind = RunKind.Xmake, XmakeTarget = "debug-fixture",
                NativeSourceMapText = remote + " => .",
            };
            window.SetConfigurations([mapped], mapped.Name);
            string source = Path.Combine(root, "main.cpp");
            window.Session.Debug.Breakpoints.Set(source, Mark(source, "locals-stop"));
            Ui.Launched(window, window.DebugAsync(), "the source-mapped native session", Limit);
            WaitForStop(window, source);
            Assert.Equal(source, window.EditorArea.Active?.Path, PathNames.Comparer);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Failed_build_leaves_diagnostics_and_starts_no_debugger()
    {
        string root = Configure("native-debug");
        string path = Path.Combine(root, "c_math.c");
        File.AppendAllText(path, "\n#error deliberate debug build failure\n");
        MainWindow window = Open(root);
        try
        {
            Assert.True(window.SelectConfiguration("Xmake: debug-fixture"));
            string? problem = Ui.Await(window.DebugAsync(), "the native build refusal", Limit);
            Assert.NotNull(problem);
            Assert.False(window.Session.Debug.IsDebugging);
            Assert.Empty(window.Session.Debug.Tabs);
            Assert.Contains("deliberate debug build failure", window.BottomPanel.Build.Document.Snapshot.ToString(), StringComparison.Ordinal);
            Assert.Contains(window.Session.Problems, issue => issue.Path is { } source && PathNames.Same(path, source));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Restart_and_stop_clean_up_owned_debuggees_and_descendants()
    {
        string root = Configure("native-debug");
        MainWindow window = Open(root);
        try
        {
            RunConfiguration waiting = new() { Name = "Waiting debug", Kind = RunKind.Xmake, XmakeTarget = "debug-fixture", Arguments = "wait" };
            window.SetConfigurations([waiting], waiting.Name);
            Ui.Launched(window, window.DebugAsync(), "the debuggee that creates a child", Limit);
            Pitstop.App.Debugging.DebugSession first = window.Session.Debug.Session!;
            using Process initialChild = LaunchedChild();
            window.BottomPanel.Debug.ShowTab(console: true);
            Capture(window, "native-debug-startup");
            Assert.True(window.Perform(AppActions.Pause));
            Ui.WaitFor(() => first.State == DebugState.Paused, "the running native program to pause", Limit);
            Assert.True(window.Perform(AppActions.Rerun));
            Ui.WaitFor(() => window.Session.Debug.Session is { State: DebugState.Running } current && current != first, "the replacement debug session", Limit);
            Assert.False(DotnetProcesses.Runs(initialChild));
            using Process replacementChild = LaunchedChild();
            Assert.NotEqual(initialChild.Id, replacementChild.Id);
            Assert.True(window.Perform(AppActions.Stop));
            Ui.WaitFor(() => !window.Session.Debug.IsDebugging, "the replacement debugger to stop", Limit);
            Assert.False(DotnetProcesses.Runs(replacementChild));
        }
        finally
        {
            window.Close();
        }

        Process LaunchedChild()
        {
            string said = string.Empty;
            Ui.WaitFor(() =>
            {
                said = window.Session.Debug.Output().ToString();
                return said.Contains("fixture ready", StringComparison.Ordinal);
            }, "the debuggee reports its created child", Limit);
            string pid = said.Split('\n').Last(line => line.StartsWith("owned-child=", StringComparison.Ordinal));
            return Process.GetProcessById(int.Parse(pid.AsSpan("owned-child=".Length).Trim(), System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [AvaloniaFact]
    public void The_native_terminal_takes_input_and_reports_real_output_and_exit()
    {
        string root = Configure("native-debug");
        MainWindow window = Open(root);
        try
        {
            RunConfiguration interactive = new()
            {
                Name = "Interactive debug", Kind = RunKind.Xmake, XmakeTarget = "debug-fixture",
                Console = ConsoleKind.Terminal, Arguments = "interactive",
            };
            window.SetConfigurations([interactive], interactive.Name);
            Ui.Launched(window, window.DebugAsync(), "LLDB's integrated terminal request", Limit);
            TerminalTab terminal = Assert.Single(window.TerminalPanel.Tabs, tab => tab.Name == interactive.Name);
            string Text() => string.Concat(terminal.Session.Screen.Scrollback.Concat(terminal.Session.Screen.Lines).Select(line => line.Text()));
            Ui.WaitFor(() => Text().Contains("fixture ready", StringComparison.Ordinal), "the interactive debuggee in ConPTY", Limit);
            terminal.Session.Write("pipeline\r");
            Ui.WaitFor(() => Text().Contains("echo=pipeline", StringComparison.Ordinal) && Text().Contains("fixture stderr", StringComparison.Ordinal),
                () => Text(), Limit);
            Ui.WaitFor(() => window.Session.Debug.Session?.State == DebugState.Ended, "terminal debuggee exit", Limit);
            Assert.Equal(17, window.Session.Debug.Session!.ExitCode);
            Capture(window, "native-debug-terminal");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Native_attach_dialog_selects_an_explicit_process_and_detach_leaves_it_running()
    {
        string root = Configure("native-debug");
        MainWindow window = Open(root);
        try
        {
            Assert.True(window.SelectConfiguration("Xmake: debug-fixture"));
            BuildOutcome built = Ui.Await(window.BuildAsync(), "the attach fixture build", Limit);
            Assert.True(built.Succeeded, string.Join('\n', built.Output));
            XmakeWorkspace snapshot = window.Session.Xmake!.Snapshot!;
            Pitstop.ProjectSystem.NativeLaunchPlan plan = Ui.Await(XmakeDebugLaunch.ResolveAsync(snapshot, "debug-fixture", new()),
                "fixture environment", Limit);
            ProcessStartInfo command = ChildProcesses.Command(plan.Executable, ["attach"], plan.WorkingDirectory);
            foreach ((string name, string value) in plan.Environment)
            {
                command.Environment[name] = value;
            }
            command.RedirectStandardInput = command.RedirectStandardOutput = true;
            using Process child = Ui.Await(ChildProcesses.StartAsync(command), "the external fixture process", Limit);
            ChildProcesses.HoldFamily(child);
            try
            {
                CancellationToken stop = TestContext.Current.CancellationToken;
                Assert.Equal("fixture ready", Ui.Await(ChildProcesses.Output(child).ReadLineAsync(stop).AsTask(), "fixture readiness", Limit));
                AttachToProcessDialog dialog = window.ShowAttachToProcess("debug-fixture");
                Ui.Await(dialog.Listed, "enumeration of local processes", Limit);
                dialog.Choose(child.Id);
                Assert.Equal(child.Id, dialog.Chosen?.Id);
                Capture(dialog, "native-debug-attach");
                dialog.Attach();
                Ui.WaitFor(() => window.Session.Debug.Session is { Attached: true, State: DebugState.Paused }, "LLDB attaches in stopped state", Limit);
                Ui.WaitFor(() => window.BottomPanel.Debug.Threads.Count >= 2, "the native process's worker thread", Limit);
                ComboBox threads = window.GetVisualDescendants().OfType<ComboBox>()
                    .Single(list => ReferenceEquals(list.ItemsSource, window.BottomPanel.Debug.Threads));
                int previousTop = window.BottomPanel.Debug.Frames[0].Id;
                threads.SelectedItem = window.BottomPanel.Debug.Threads.First(thread => thread.Id != window.Session.Debug.Session!.StoppedThread);
                Ui.WaitFor(() => window.BottomPanel.Debug.Frames.FirstOrDefault() is { } top && top.Id != previousTop,
                    "the selected worker's own stack", Limit);
                Capture(window, "native-debug-threads");
                Assert.True(window.Perform(AppActions.Detach));
                Ui.WaitFor(() => !window.Session.Debug.IsDebugging, "safe native detach", Limit);
                Assert.True(DotnetProcesses.Runs(child));
                Ui.Await(ChildProcesses.Input(child).WriteLineAsync("exit"), "release the detached test fixture", Limit);
                Ui.Await(child.WaitForExitAsync(stop), "the detached fixture exits independently", Limit);
                Assert.Equal(0, child.ExitCode);
            }
            finally
            {
                ChildProcesses.Kill(child);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Explicit_adapter_setting_reports_the_reference_adapter_and_an_actionable_missing_path()
    {
        string root = Configure("native-debug");
        MainWindow window = Open(root);
        try
        {
            string llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW")!;
            string adapter = Environment.GetEnvironmentVariable(LldbDiscovery.AdapterVariable) ?? Path.Combine(llvm, "bin", "lldb-dap.exe");
            Inspect(adapter, "LLDB 23.1.6", "native-debug-settings");
            Inspect(Path.Combine(root, "missing-lldb-dap.exe"), "Set PITSTOP_LLDB_DAP", "native-debug-missing-adapter");
        }
        finally
        {
            window.Close();
        }

        void Inspect(string path, string expected, string screenshot)
        {
            window.ApplySettings(window.Settings with { NativeTools = window.Settings.NativeTools with { LldbDap = path } });
            SettingsDialog dialog = window.ShowSettings();
            dialog.Page = SettingsDialog.NativeToolchainsPage;
            Ui.Settle();
            Ui.ClickButton(dialog, "Detect Toolchains");
            TextBlock report = dialog.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "NativeToolchainReport");
            Ui.WaitFor(() => report.Text?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true,
                () => report.Text ?? "native toolchain report", Limit);
            ScrollViewer scroll = report.GetVisualAncestors().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            Capture(dialog, screenshot);
            dialog.Close();
        }
    }

    private static MainWindow Open(string root)
    {
        MainWindow window = new(managedDevelopment: false) { Width = 1360, Height = 900, Autosaves = false };
        window.Show();
        window.OpenFolder(root);
        Ui.WaitFor(() => window.Session.Xmake is { IsEvaluating: false, Snapshot: not null }, "Xmake workspace", Limit);
        return window;
    }

    private static void WaitForStop(MainWindow window, string path) => Ui.WaitFor(
        () => window.Session.Debug.Session is { Engine: DebugEngine.Native, State: DebugState.Paused }
            && window.Session.Debug.Stop?.Top?.Source?.Path is { } at && PathNames.Same(path, at),
        () => $"LLDB stop in {Path.GetFileName(path)}, actual {window.Session.Debug.Session?.State}/{window.Session.Debug.Stop?.Top?.Source?.Path}, "
            + $"breakpoints {string.Join(", ", window.Session.Debug.Breakpoints.In(path).Select(point => $"{point.Line}: {point.Status}"))}\n"
            + $"{Ui.Said(window)}\n{window.Session.Debug.Output()}",
        TimeSpan.FromSeconds(50));

    private static int Mark(string path, string marker) => File.ReadLines(path)
        .Select((line, index) => (line, index)).Single(line => line.line.Contains("// " + marker, StringComparison.Ordinal)).index;

    internal static string Configure(string fixture, params string[] flags)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64, "Native debugger integration requires Windows x64.");
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        string? xmake = Environment.GetEnvironmentVariable("PITSTOP_XMAKE");
        Assert.SkipUnless(llvm is not null && Directory.Exists(llvm) && File.Exists(xmake), "Set PITSTOP_TEST_LLVM_MINGW and PITSTOP_XMAKE to the reference tools.");
        string adapter = Environment.GetEnvironmentVariable(LldbDiscovery.AdapterVariable) ?? Path.Combine(llvm!, "bin", "lldb-dap.exe");
        Assert.SkipUnless(File.Exists(adapter), "The LLVM-MinGW profile needs lldb-dap.exe. Configure PITSTOP_LLDB_DAP.");
        string root = PathNames.Canonical(TestFolders.Create("integrated-lldb-"));
        string source = Path.Combine(Debuggees.Root, "tests", "CppWorkspaces", fixture);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }
        if (fixture == "native-debug")
        {
            Directory.CreateDirectory(Path.Combine(root, "run-directory"));
        }
        ProcessStartInfo configure = ChildProcesses.Command(xmake!,
            ["f", "-m", "debug", "-p", "mingw", "--toolchain=llvm", $"--mingw={llvm}", $"--sdk={llvm}", "--require=no", "-y", .. flags], root);
        configure.Environment["XMAKE_STATS"] = "false";
        ChildResult result = Ui.Await(ChildProcesses.RunAsync(configure, limit: Limit, cancel: TestContext.Current.CancellationToken), "test fixture configuration", Limit);
        Assert.True(result.Succeeded, result.Output + result.Error);
        return root;
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("PITSTOP_TEST_DEBUG_SHOTS") is not { Length: > 0 } destination)
        {
            return;
        }
        Directory.CreateDirectory(destination);
        ThemeVariant? previous = window.RequestedThemeVariant;
        foreach ((string label, ThemeVariant theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            window.RequestedThemeVariant = theme;
            Ui.Settle();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The native debugger window has no frame.");
            frame.Save(Path.Combine(destination, $"{name}-{label}.png"), PngBitmapEncoderOptions.Default);
        }
        window.RequestedThemeVariant = previous;
    }
}

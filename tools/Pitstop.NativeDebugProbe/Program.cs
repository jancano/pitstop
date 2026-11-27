using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.NativeDebug;
using Pitstop.ProjectSystem;
using Pitstop.Xmake;

namespace Pitstop.NativeDebugProbe;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(DapConnection.Options) { WriteIndented = true };

    internal static async Task<int> Main(string[] arguments)
    {
        using CancellationTokenSource cancel = new(TimeSpan.FromMinutes(15));
        Console.CancelKeyPress += (_, args) => { args.Cancel = true; cancel.Cancel(); };
        if (arguments is not ["--trust-fixtures", string llvmRoot, string artifactDirectory])
        {
            Console.Error.WriteLine("From the repository root, pass --trust-fixtures <llvm-mingw-root> <output-directory>. Set PITSTOP_XMAKE and optionally PITSTOP_LLDB_DAP.");
            return 2;
        }

        string repository = Path.GetFullPath(".");
        llvmRoot = Path.GetFullPath(llvmRoot);
        string output = Path.GetFullPath(artifactDirectory);
        if (!File.Exists(Path.Combine(repository, "Pitstop.slnx")) || !Directory.Exists(Path.Combine(llvmRoot, "bin")))
        {
            Console.Error.WriteLine("Run from the repository root and provide the existing LLVM-MinGW distribution.");
            return 2;
        }

        string root = Directory.CreateTempSubdirectory("pitstop-native-debug-").FullName;
        string temp = Path.GetFullPath(Path.GetTempPath());
        WorkspaceTrust.Trust([root]);
        Directory.CreateDirectory(output);
        try
        {
            return await new DebugProbe(repository, llvmRoot, root, output, cancel.Token).RunAsync();
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Native debugger probe canceled or exceeded its overall time limit.");
            return 130;
        }
        catch (NativeDebugException error)
        {
            Console.Error.WriteLine($"{error.Failure}: {error.Message}");
            return 1;
        }
        finally
        {
            WorkspaceTrust.TakeBack(root);
            if (PathNames.Same(Path.GetDirectoryName(root)!, Path.TrimEndingDirectorySeparator(temp))
                && Path.GetFileName(root).StartsWith("pitstop-native-debug-", StringComparison.Ordinal))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    internal static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + "\n");
}

internal sealed partial class DebugProbe(string repository, string llvmRoot, string scratch, string output, CancellationToken cancel)
{
    private readonly List<Check> checks = [];
    private LldbInstallation installation = null!;

    internal async Task<int> RunAsync()
    {
        installation = await LldbDiscovery.DiscoverAsync(llvmBin: Path.Combine(llvmRoot, "bin"), cancel: cancel);
        Program.Write(Path.Combine(output, "profile.json"), new
        {
            host = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            adapter = installation.AdapterVersion.ToString(),
            lldb = installation.LldbVersion.ToString(),
            bundledLibrary = installation.RuntimeLibrary is not null,
        });
        await ScenarioAsync("basic", "native-debug", "debug-fixture", DebugBasicAsync);
        await ScenarioAsync("modules23", "native-debug-modules", "debug-modules", DebugModulesAsync);
        await ScenarioAsync("modules26", "native-debug-modules", "debug-modules", DebugModulesAsync, flags: ["--cpp26=y"]);
        await ScenarioAsync("release", "native-debug", "debug-fixture", InspectReleaseAsync, mode: "release", optional: true);
        foreach ((string name, ConcurrentQueue<NativeDebugPacket> trace) in transcripts)
        {
            Program.Write(Path.Combine(output, name + "-dap.json"), trace.ToArray());
        }
        Program.Write(Path.Combine(output, "results.json"), checks);
        return checks.Any(check => check.Status == "failed") ? 1 : 0;
    }

    private async Task ScenarioAsync(string name, string fixture, string target,
        Func<NativeDebugSession, NativeLaunchPlan, string, Task> inspect, string mode = "debug", bool optional = false,
        params string[] flags)
    {
        ConcurrentQueue<NativeDebugPacket> trace = new();
        try
        {
            string workspace = Path.Combine(scratch, name);
            Directory.CreateDirectory(workspace);
            string source = Path.Combine(repository, "tests", "CppWorkspaces", fixture);
            foreach (string path in Directory.EnumerateFiles(source))
            {
                File.Copy(path, Path.Combine(workspace, Path.GetFileName(path)));
            }
            if (fixture == "native-debug")
            {
                Directory.CreateDirectory(Path.Combine(workspace, "run-directory"));
            }
            XmakeService xmake = new();
            XmakeInstallation tool = await xmake.DiscoverAsync(scratch, cancel);
            ProcessStartInfo configure = ChildProcesses.Command(tool.Executable,
                ["f", "-m", mode, "-p", "mingw", "--toolchain=llvm", $"--mingw={llvmRoot}", $"--sdk={llvmRoot}", "--require=no", "-y", .. flags],
                workspace);
            configure.Environment["XMAKE_STATS"] = "false";
            await ExecuteAsync(configure, "Xmake debug configuration");
            XmakeWorkspace evaluated = await xmake.EvaluateAsync(workspace, Path.Combine(scratch, name + "-metadata"), cancel);
            NativeLaunchPlan plan = await XmakeDebugLaunch.ResolveAsync(evaluated, target, new(), cancel);
            await using (NativeDebugSession session = await NativeDebugSession.StartAsync(installation, workspace, scratch, cancel, trace.Enqueue))
            {
                Program.Write(Path.Combine(output, "capabilities.json"), session.AdvertisedCapabilities.EnumerateObject()
                    .Where(value => value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    .ToDictionary(value => value.Name, value => value.Value.GetBoolean()));
                await inspect(session, plan, name);
            }
            Record(name, "passed");
            if (name == "basic")
            {
                await RuntimeChecksAsync(evaluated, plan);
            }
            if (fixture == "native-debug-modules")
            {
                await VerifyAsync(name + "-partition-stepping", () => StepIntoPartitionsAsync(name, plan));
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            string evidence = error switch
            {
                NativeDebugException failure => failure.Failure.ToString(),
                DapException dap => $"DAP {dap.Command} failed",
                XmakeException => "Xmake runtime export or evaluation failed",
                ProbeFailure failed => failed.Message,
                _ => error.GetType().Name,
            };
            Record(name, optional ? "optional-failure" : "failed", evidence);
        }
        finally
        {
            Program.Write(Path.Combine(output, name + "-dap.json"), trace.ToArray());
        }
    }

    private async Task DebugBasicAsync(NativeDebugSession session, NativeLaunchPlan plan, string scenario)
    {
        string root = plan.Workspace.Root;
        DapBreakpoint[] points = [];
        await session.LaunchAsync(plan, async (adapter, token) =>
        {
            points = [await BreakpointAsync(adapter, root, "main.cpp", "locals-stop", token),
                await BreakpointAsync(adapter, root, "c_math.c", "c-stop", token),
                await BreakpointAsync(adapter, root, "library.cpp", "library-stop", token)];
        }, Build(plan), cancel);
        Require(points[0].Verified && points[1].Verified, "C and C++ source breakpoints must verify.");
        await InspectImageAsync(plan, symbolsExpected: true);
        (StoppedEvent stopped, DapStackFrame frame) = await StopAsync(session, "main.cpp");
        Require(frame.Name.Contains("inspect", StringComparison.Ordinal), "The first C++ frame must name inspect.");
        ThreadsResponse threads = await session.RequestAsync<ThreadsResponse>(DapCommands.Threads, cancel: cancel);
        Require(threads.Threads.Any(thread => thread.Id == stopped.ThreadId), "The stopped thread must be present in threads.");
        DapVariable[] variables = await VariablesAsync(session, frame.Id);
        Require(variables.Any(value => value is { Name: "seed", Value: "11" })
            && variables.Any(value => value.Name == "value"), "The stopped scope must contain the argument and a local.");
        EvaluateResponse expression = await session.RequestAsync<EvaluateResponse>(DapCommands.Evaluate,
            new EvaluateArguments("seed + 2", frame.Id, EvaluateContexts.Watch), cancel);
        Require(expression.Result == "13", "Explicit arithmetic in the selected frame returned an unexpected value.");
        Program.Write(Path.Combine(output, scenario + "-variables.json"), variables.Select(value =>
            new { value.Name, value.Type, CanExpand = value.VariablesReference != 0 }).ToArray());
        await InspectOptionalAsync(session, frame, variables);
        await session.RequestAsync<JsonElement>(DapCommands.StepIn, new StepInArguments(stopped.ThreadId!.Value), cancel);
        (stopped, _) = await StopAsync(session);
        await session.RequestAsync<JsonElement>(DapCommands.StepOut, new ThreadArguments(stopped.ThreadId!.Value), cancel);
        (stopped, _) = await StopAsync(session);
        await session.RequestAsync<JsonElement>(DapCommands.Next, new ThreadArguments(stopped.ThreadId!.Value), cancel);
        (stopped, _) = await StopAsync(session);
        await session.RequestAsync<ContinueResponse>(DapCommands.Continue, new ThreadArguments(stopped.ThreadId!.Value), cancel);
        string messages = await UntilExitAsync(session, expected: 31);
        Require(RuntimeMatches(messages, "run-directory", "configured argument"), "DAP launch did not preserve the default Xmake runtime configuration.");
    }

    private async Task DebugModulesAsync(NativeDebugSession session, NativeLaunchPlan plan, string scenario)
    {
        (string File, string Marker)[] locations = [("main.cpp", "consumer-stop"), ("facade.cppm", "facade-stop"),
            ("implementation.cpp", "implementation-stop"), ("values.cppm", "interface-stop"),
            ("partition.cppm", "partition-stop"), ("detail.cppm", "internal-stop"), ("main.cpp", "std-stop")];
        await session.LaunchAsync(plan, async (adapter, token) =>
        {
            foreach (IGrouping<string, (string File, string Marker)> group in locations.GroupBy(location => location.File))
            {
                string path = Path.Combine(plan.Workspace.Root, group.Key);
                SetBreakpointsResponse response = await adapter.RequestAsync<SetBreakpointsResponse>(DapCommands.SetBreakpoints,
                    new SetBreakpointsArguments(new DapSource { Path = path },
                        group.Select(location => new DapSourceBreakpoint(Line(path, location.Marker))).ToArray()), token);
                Require(response.Breakpoints.Count == group.Count() && response.Breakpoints.All(point => point.Verified),
                    $"Module breakpoint did not verify in {group.Key}.");
            }
        }, Build(plan), cancel);

        foreach ((string file, _) in locations)
        {
            (StoppedEvent stopped, DapStackFrame frame) = await StopAsync(session, file);
            Require(frame.Source?.Path is { } source && PathNames.Same(source, Path.Combine(plan.Workspace.Root, file)),
                "The module frame must report the fixture source path.");
            if (frame.Line == Line(Path.Combine(plan.Workspace.Root, "main.cpp"), "std-stop") && file == "main.cpp")
            {
                DapVariable[] variables = await VariablesAsync(session, frame.Id);
                Require(variables.Any(value => value.Name == "answer" && value.Type?.Contains("Answer", StringComparison.Ordinal) == true)
                    && variables.Any(value => value.Name == "values" && value.Type?.Contains("vector", StringComparison.Ordinal) == true),
                    "The import std consumer should expose its vector and module-exported type.");
            }
            string next = file == "main.cpp" && frame.Line == Line(Path.Combine(plan.Workspace.Root, file), "consumer-stop")
                ? DapCommands.StepIn : DapCommands.Continue;
            await session.RequestAsync<JsonElement>(next, new ThreadArguments(stopped.ThreadId!.Value), cancel);
        }
        await UntilExitAsync(session, expected: 0);
    }

    private async Task StepIntoPartitionsAsync(string scenario, NativeLaunchPlan plan)
    {
        (string Marker, string File)[] paths = [("partition-call", "partition.cppm"), ("internal-call", "detail.cppm")];
        string source = Path.Combine(plan.Workspace.Root, "implementation.cpp");
        int[] calls = [.. paths.Select(path => Line(source, path.Marker))];
        await using NativeDebugSession session = await StartRuntimeAsync(scenario + "-partition-stepping", plan);
        await session.LaunchAsync(plan with { BuildBeforeDebug = false }, async (adapter, token) =>
        {
            SetBreakpointsResponse response = await adapter.RequestAsync<SetBreakpointsResponse>(DapCommands.SetBreakpoints,
                new SetBreakpointsArguments(new DapSource { Path = source }, calls.Select(line => new DapSourceBreakpoint(line)).ToArray()), token);
            Require(response.Breakpoints.Count == calls.Length && response.Breakpoints.All(point => point.Verified),
                "The call sites into both partitions must bind.");
        }, cancel: cancel);
        for (int index = 0; index < paths.Length; ++index)
        {
            (StoppedEvent stopped, DapStackFrame caller) = await StopAsync(session, "implementation.cpp");
            Require(caller.Line == calls[index], "The stop did not identify the call into the partition.");
            await session.RequestAsync<JsonElement>(DapCommands.StepIn, new StepInArguments(stopped.ThreadId!.Value), cancel);
            (StoppedEvent stepped, DapStackFrame callee) = await StopAsync(session, paths[index].File);
            Require(stepped.Reason == StopReasons.Step && callee.Source?.Path is { } path
                && PathNames.Same(path, Path.Combine(plan.Workspace.Root, paths[index].File)), "Step Into did not enter the partition source.");
            await session.RequestAsync<ContinueResponse>(DapCommands.Continue, new ThreadArguments(stepped.ThreadId!.Value), cancel);
        }
        await UntilExitAsync(session, expected: 0);
    }

    private static async Task<DapBreakpoint> BreakpointAsync(NativeDebugSession session, string root, string file, string marker, CancellationToken cancel)
    {
        string path = Path.Combine(root, file);
        SetBreakpointsResponse result = await session.RequestAsync<SetBreakpointsResponse>(DapCommands.SetBreakpoints,
            new SetBreakpointsArguments(new DapSource { Path = path }, [new DapSourceBreakpoint(Line(path, marker))]), cancel);
        return result.Breakpoints.Single();
    }

    private async Task<(StoppedEvent Stop, DapStackFrame Frame)> StopAsync(NativeDebugSession session, string? expectedFile = null,
        Action<OutputEvent>? onOutput = null)
    {
        NativeDebugEvent next;
        do
        {
            next = await session.ReadEventAsync(cancel);
            Require(next.Name is not (DapEvents.Exited or DapEvents.Terminated), "The program exited before the expected stop.");
            if (onOutput is not null && next.Name == DapEvents.Output
                && next.Body.Deserialize<OutputEvent>(DapConnection.Options) is { } message)
            {
                onOutput(message);
            }
        } while (next.Name != DapEvents.Stopped);
        StoppedEvent stopped = next.Body.Deserialize<StoppedEvent>(DapConnection.Options)!;
        Require(stopped.ThreadId is not null, "The stopped event did not identify a thread.");
        StackTraceResponse trace = await session.RequestAsync<StackTraceResponse>(DapCommands.StackTrace,
            new StackTraceArguments(stopped.ThreadId!.Value, Levels: 8), cancel);
        DapStackFrame frame = trace.StackFrames.First();
        Require(expectedFile is null || Path.GetFileName(frame.Source?.Path) == expectedFile, $"Expected a stop in {expectedFile}.");
        return (stopped, frame);
    }

    private async Task<DapVariable[]> VariablesAsync(NativeDebugSession session, int frame)
    {
        ScopesResponse scopes = await session.RequestAsync<ScopesResponse>(DapCommands.Scopes, new ScopesArguments(frame), cancel);
        List<DapVariable> variables = [];
        foreach (DapScope scope in scopes.Scopes.Where(scope => !scope.Expensive && scope.Name != "Registers"))
        {
            VariablesResponse read = await session.RequestAsync<VariablesResponse>(DapCommands.Variables,
                new VariablesArguments(scope.VariablesReference), cancel);
            variables.AddRange(read.Variables);
        }
        return [.. variables];
    }

    private async Task<string> UntilExitAsync(NativeDebugSession session, int expected)
    {
        StringBuilder messages = new();
        while (true)
        {
            NativeDebugEvent next = await session.ReadEventAsync(cancel);
            if (next.Name == DapEvents.Exited)
            {
                int actual = next.Body.Deserialize<ExitedEvent>(DapConnection.Options)!.ExitCode;
                Require(actual == expected, $"Expected fixture exit code {expected}, received {actual}.");
                return messages.ToString();
            }
            if (next.Name == DapEvents.Output && messages.Length < 64 * 1024)
            {
                messages.Append(next.Body.Deserialize<OutputEvent>(DapConnection.Options)?.Output);
            }
            if (next.Name == DapEvents.Stopped)
            {
                StoppedEvent? stopped = next.Body.Deserialize<StoppedEvent>(DapConnection.Options);
                await session.RequestAsync<ContinueResponse>(DapCommands.Continue, new ThreadArguments(stopped!.ThreadId!.Value), cancel);
            }
            Require(next.Name != DapEvents.Terminated, "LLDB terminated without an exit code.");
        }
    }

    private Func<CancellationToken, Task> Build(NativeLaunchPlan plan) => token =>
        ExecuteAsync(XmakeOperations.Build((XmakeWorkspace)plan.Workspace, XmakeBuildKind.Build, [plan.Target]), "Xmake build before debug", token);

    private async Task ExecuteAsync(ProcessStartInfo command, string operation, CancellationToken operationCancel = default)
    {
        ChildResult result = await ChildProcesses.RunAsync(command, limit: TimeSpan.FromMinutes(3),
            cancel: operationCancel.CanBeCanceled ? operationCancel : cancel, mostOutput: 4 * 1024 * 1024);
        Require(result.Succeeded, $"{operation} exited with code {result.ExitCode}.");
    }

    private static int Line(string path, string marker) =>
        File.ReadLines(path).Select((text, index) => (text, index)).Single(line => line.text.Contains("// " + marker, StringComparison.Ordinal)).index + 1;

    private void Record(string name, string status, string? detail = null)
    {
        checks.Add(new(name, status, detail));
        Console.WriteLine(detail is null ? $"{name}: {status}" : $"{name}: {status} ({detail})");
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition)
        {
            throw new ProbeFailure(failure);
        }
    }

    private sealed record Check(string Name, string Status, string? Detail);
    private sealed class ProbeFailure(string message) : Exception(message);
}

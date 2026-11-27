using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.NativeDebug;
using Pitstop.ProjectSystem;
using Pitstop.Xmake;

namespace Pitstop.NativeDebugProbe;

internal sealed partial class DebugProbe
{
    private async Task RuntimeChecksAsync(XmakeWorkspace workspace, NativeLaunchPlan plan)
    {
        await VerifyAsync("runtime-xmake", async () =>
        {
            ProcessStartInfo command = XmakeOperations.Run(workspace, plan.Target, [], "");
            ChildResult result = await ChildProcesses.RunAsync(command, limit: TimeSpan.FromSeconds(20), cancel: cancel);
            Require(RuntimeMatches(result.Output, "run-directory", "configured argument") && result.ExitCode != 0,
                "Xmake did not report the expected default runtime settings and nonzero result.");
        });
        await VerifyAsync("runtime-custom-hook", async () =>
        {
            try
            {
                await XmakeDebugLaunch.ResolveAsync(workspace, "custom-run", new(), cancel);
            }
            catch (XmakeException error) when (error.Message.Contains("custom Xmake run hook", StringComparison.Ordinal))
            {
                return;
            }
            throw new ProbeFailure("A target with custom on_run must require a separate launch strategy.");
        });
        await VerifyAsync("runtime-overrides", async () =>
        {
            NativeLaunchOptions options = new(["space here", "a\"b", "", "café λ", "--option"],
                ".", ImmutableDictionary<string, string>.Empty.Add("PITSTOP_FIXTURE_LIST", "tail"), BuildBeforeDebug: false);
            NativeLaunchPlan customized = await XmakeDebugLaunch.ResolveAsync(workspace, plan.Target, options, cancel);
            await using NativeDebugSession session = await StartRuntimeAsync("runtime-overrides", plan);
            await session.LaunchAsync(customized, cancel: cancel);
            string messages = await UntilExitAsync(session, 31);
            Require(RuntimeMatches(messages, "basic", "space here", "first;second;tail"), "The overrides did not preserve the working directory and environment.");
            Require(messages.Contains("arg[2]=[a\"b]", StringComparison.Ordinal), "The target did not receive the embedded quote intact.");
            Require(messages.Contains("arg[3]=[]", StringComparison.Ordinal), "The target did not receive the empty argument.");
            Require(messages.Contains("arg[4]=[café λ]", StringComparison.Ordinal), "The target did not receive the Unicode argument intact.");
            Require(messages.Contains("arg[5]=[--option]", StringComparison.Ordinal), "The target did not receive the final argument intact.");
        });
        await VerifyAsync("stdio-files", async () =>
        {
            string input = Path.Combine(scratch, "input.txt");
            string actual = Path.Combine(scratch, "stdout.txt");
            string errors = Path.Combine(scratch, "stderr.txt");
            await File.WriteAllTextAsync(input, "café λ\n", cancel);
            await using (NativeDebugSession session = await StartRuntimeAsync("stdio-files", plan))
            {
                await session.LaunchAsync(plan with { Arguments = ["interactive"], BuildBeforeDebug = false },
                    cancel: cancel, stdio: new(input, actual, errors));
                await UntilExitAsync(session, 17);
            }
            Require(File.Exists(actual) && File.Exists(errors)
                && (await File.ReadAllTextAsync(actual, cancel)).Contains("echo=café λ", StringComparison.Ordinal)
                && (await File.ReadAllTextAsync(errors, cancel)).Contains("fixture stderr", StringComparison.Ordinal),
                "LLDB did not preserve file-backed input, output, and error streams.");
        });
        await VerifyAsync("terminal-reverse-request", () => TerminalProbeAsync(plan), optional: true);
        await VerifyAsync("attach-detach", () => AttachProbeAsync(plan));
        await VerifyAsync("pause-and-cleanup", () => PauseProbeAsync(plan, crashAdapter: false));
        await VerifyAsync("adapter-crash-cleanup", () => PauseProbeAsync(plan, crashAdapter: true));
        await VerifyAsync("function-breakpoint", () => FunctionBreakpointAsync(plan), optional: true);
        await VerifyAsync("conditional-hit-log-breakpoints", () => ConditionalBreakpointsAsync(plan), optional: true);
        await VerifyAsync("cpp-exception", () => ExceptionProbeAsync(plan), optional: true);
        await VerifyAsync("dynamic-breakpoint", async () =>
        {
            DapBreakpoint? initial = null;
            await using NativeDebugSession session = await StartRuntimeAsync("dynamic-breakpoint", plan);
            await session.LaunchAsync(plan with { Arguments = ["dynamic-library"], BuildBeforeDebug = false }, async (adapter, token) =>
            {
                initial = await BreakpointAsync(adapter, workspace.Root, "late_library.cpp", "late-library-stop", token);
            }, cancel: cancel);
            Require(initial is { Verified: false }, "Expected a pending source breakpoint before the DLL loads.");
            (StoppedEvent stopped, DapStackFrame frame) = await StopAsync(session, "late_library.cpp");
            Require(frame.Line == Line(Path.Combine(workspace.Root, "late_library.cpp"), "late-library-stop"),
                "The dynamically loaded DLL stopped at a different line.");
            await session.RequestAsync<ContinueResponse>(DapCommands.Continue, new ThreadArguments(stopped.ThreadId!.Value), cancel);
            await UntilExitAsync(session, 12);
        }, optional: true);
    }

    private static bool RuntimeMatches(string output, string cwd, string first, string list = "first;second") =>
        output.Contains($"arg[1]=[{first}]", StringComparison.Ordinal)
        && output.Contains($"cwd={cwd}", StringComparison.Ordinal)
        && output.Contains("mode=evaluated", StringComparison.Ordinal)
        && output.Contains($"list={list}", StringComparison.Ordinal);

    private async Task<NativeDebugSession> StartRuntimeAsync(string check, NativeLaunchPlan plan,
        Func<RunInTerminalArguments, CancellationToken, Task<NativeDebugTerminalLaunch>>? launcher = null)
    {
        ConcurrentQueue<NativeDebugPacket> trace = new();
        NativeDebugSession session = await NativeDebugSession.StartAsync(installation, plan.Workspace.Root, scratch, cancel,
            trace.Enqueue, launcher);
        transcripts.Add((check, trace));
        return session;
    }

    private readonly List<(string Name, ConcurrentQueue<NativeDebugPacket> Trace)> transcripts = [];

    private async Task AttachProbeAsync(NativeLaunchPlan plan)
    {
        ProcessStartInfo command = ChildProcesses.Command(plan.Executable, ["attach"], plan.WorkingDirectory);
        foreach ((string name, string value) in plan.Environment)
        {
            command.Environment[name] = value;
        }
        command.RedirectStandardOutput = command.RedirectStandardInput = true;
        using Process target = await ChildProcesses.StartAsync(command);
        ChildProcesses.HoldFamily(target);
        try
        {
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(TimeSpan.FromSeconds(15));
            Require(await ChildProcesses.Output(target).ReadLineAsync(limit.Token) == "fixture ready",
                "The attach fixture did not report readiness.");
            await using (NativeDebugSession session = await StartRuntimeAsync("attach-detach", plan))
            {
                await session.AttachAsync(target.Id, cancel: cancel);
                (StoppedEvent stopped, _) = await StopAsync(session);
                await VerifyThreadsAsync(session, stopped);
            }
            Require(DotnetProcesses.Runs(target), "Disconnect terminated a user-attached process.");
            await ChildProcesses.Input(target).WriteLineAsync("exit");
            await target.WaitForExitAsync(cancel).WaitAsync(TimeSpan.FromSeconds(10), cancel);
            Require(target.ExitCode == 0, "The detached process did not resume and exit independently.");
        }
        finally
        {
            ChildProcesses.Kill(target);
        }
    }

    private async Task PauseProbeAsync(NativeLaunchPlan plan, bool crashAdapter)
    {
        await using NativeDebugSession session = await StartRuntimeAsync(crashAdapter ? "adapter-crash-cleanup" : "pause-and-cleanup", plan);
        await session.LaunchAsync(plan with { Arguments = ["wait"], BuildBeforeDebug = false }, cancel: cancel);
        int? pid = null;
        StringBuilder messages = new();
        do
        {
            NativeDebugEvent message = await session.ReadEventAsync(cancel);
            if (message.Name == DapEvents.Process)
            {
                pid = message.Body.Deserialize<ProcessEvent>(DapConnection.Options)?.SystemProcessId;
            }
            if (message.Name == DapEvents.Output)
            {
                messages.Append(message.Body.Deserialize<OutputEvent>(DapConnection.Options)?.Output);
                if (messages.ToString().Contains("fixture ready", StringComparison.Ordinal))
                {
                    break;
                }
            }
            Require(message.Name is not (DapEvents.Exited or DapEvents.Terminated), "The pause fixture stopped before it was ready.");
        } while (true);
        Require(pid is not null, "LLDB did not report an owned process id for launch.");
        using Process target = Process.GetProcessById(pid!.Value);
        using Process adapter = Process.GetProcessById(session.AdapterId);
        const string prefix = "owned-child=";
        string? childLine = messages.ToString().Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        Require(int.TryParse(childLine?[prefix.Length..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int childPid),
            "The fixture did not create an owned child process.");
        using Process child = Process.GetProcessById(childPid);
        if (crashAdapter)
        {
            // Simulate a transport failure without closing the process job through ChildProcesses.Kill.
            adapter.Kill();
            await Task.WhenAll(target.WaitForExitAsync(cancel), child.WaitForExitAsync(cancel)).WaitAsync(TimeSpan.FromSeconds(10), cancel);
            try
            {
                while (true)
                {
                    await session.ReadEventAsync(cancel);
                }
            }
            catch (NativeDebugException error) when (error.Failure == NativeDebugFailure.Adapter)
            {
                return;
            }
        }
        ThreadsResponse response = await session.RequestAsync<ThreadsResponse>(DapCommands.Threads, cancel: cancel);
        DapThread thread = response.Threads.First();
        await session.RequestAsync<JsonElement>(DapCommands.Pause, new ThreadArguments(thread.Id), cancel);
        (StoppedEvent stopped, _) = await StopAsync(session);
        await VerifyThreadsAsync(session, stopped);
        await session.RequestAsync<ContinueResponse>(DapCommands.Continue, new ThreadArguments(stopped.ThreadId!.Value), cancel);
        WorkspaceTrust.TakeBack(scratch);
        try
        {
            await session.DisposeAsync();
            await Task.WhenAll(target.WaitForExitAsync(cancel), adapter.WaitForExitAsync(cancel), child.WaitForExitAsync(cancel))
                .WaitAsync(TimeSpan.FromSeconds(10), cancel);
            try
            {
                await session.ReadEventAsync(cancel);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                return;
            }
            throw new ProbeFailure("The retired session still returned an event.");
        }
        finally
        {
            WorkspaceTrust.Trust([scratch]);
        }
    }

    private async Task VerifyThreadsAsync(NativeDebugSession session, StoppedEvent stopped)
    {
        ThreadsResponse response = await session.RequestAsync<ThreadsResponse>(DapCommands.Threads, cancel: cancel);
        Require(response.Threads.Count >= 2 && response.Threads.Any(thread => thread.Id == stopped.ThreadId),
            "LLDB did not enumerate the main thread and worker.");
        foreach (DapThread thread in response.Threads.Take(2))
        {
            StackTraceResponse stack = await session.RequestAsync<StackTraceResponse>(DapCommands.StackTrace,
                new StackTraceArguments(thread.Id, Levels: 4), cancel);
            Require(stack.StackFrames.Count > 0, "LLDB did not report frames for both threads.");
        }
    }

    private async Task TerminalProbeAsync(NativeLaunchPlan plan)
    {
        bool launched = false;
        Task<string>? stdout = null;
        await using NativeDebugSession session = await StartRuntimeAsync("terminal-reverse-request", plan, async (request, token) =>
        {
            ProcessStartInfo command = ChildProcesses.Command(request.Args[0], request.Args.Skip(1), request.Cwd ?? plan.WorkingDirectory);
            if (request.Env is { } environment)
            {
                foreach ((string name, string value) in environment)
                {
                    command.Environment[name] = value;
                }
            }
            command.RedirectStandardInput = command.RedirectStandardOutput = command.RedirectStandardError = true;
            token.ThrowIfCancellationRequested();
            Process target = await ChildProcesses.StartAsync(command);
            ChildProcesses.HoldFamily(target);
            launched = true;
            Task<string> read = ChildProcesses.Output(target).ReadToEndAsync(token);
            stdout = read;
            Task drained = ChildProcesses.Hear(target, errors: _ => { });
            await ChildProcesses.Input(target).WriteLineAsync("café λ");
            return new(new(target.Id), async () =>
            {
                try
                {
                    ChildProcesses.Kill(target);
                    await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                    await ChildProcesses.OutputEndsAsync(target, Task.WhenAll(read, drained));
                }
                finally
                {
                    target.Dispose();
                }
            });
        });
        await session.LaunchAsync(plan with { Console = NativeConsole.IntegratedTerminal, Arguments = ["interactive"], BuildBeforeDebug = false }, cancel: cancel);
        string messages = await UntilExitAsync(session, 17);
        Require(launched, "The adapter did not send runInTerminal.");
        if (!messages.Contains("echo=café λ", StringComparison.Ordinal) && stdout is not null)
        {
            messages += await stdout.WaitAsync(TimeSpan.FromSeconds(3), cancel);
        }
        Require(messages.Contains("echo=café λ", StringComparison.Ordinal), "DAP output did not contain the terminal echo.");
    }

    private async Task VerifyAsync(string name, Func<Task> probe, bool optional = false)
    {
        try
        {
            await probe();
            Record(name, "passed");
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            string reason = error switch
            {
                ProbeFailure failed => failed.Message,
                DapException dap => $"DAP {dap.Command} failed",
                NativeDebugException failed => $"{failed.Failure}: {failed.Message}",
                _ => error.GetType().Name,
            };
            Record(name, optional ? "optional-failure" : "failed", reason);
        }
    }
}

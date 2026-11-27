using System.ComponentModel;
using ModelContextProtocol.Server;
using Pitstop.App.Debugging;
using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.NativeDebug;

namespace Pitstop.App.Mcp;

public sealed partial class PitstopTools
{
    [McpServerTool(Name = McpTools.NativeDebugInspect, Title = "Inspect the native debugger", ReadOnly = true), Description(
        "Read the IDE's current LLDB session. Operations: availability, state, breakpoints, wait, threads, frames, scopes, variables, module. Use reference for a thread, a frame or a variables reference, according to the operation. Use sessionId and stop from state to guard old references. Results are bounded.")]
    public Task<object> NativeDebugInspect(string operation = "state", int? reference = null, string? path = null,
        string? sessionId = null, int? stop = null, int limit = 50, int waitSeconds = 10, CancellationToken cancel = default) =>
        OnUi<object>(cancel, async () =>
        {
            if (!NativeTrusted(out string folder))
            {
                return new { status = "Open a trusted Xmake workspace." };
            }
            DebugController debugger = Session.Debug;
            int count = Math.Clamp(limit, 1, 100);
            if (operation == "availability")
            {
                if (Builds.WhyNotDebugXmake(Session.Configurations.Chosen) is { } unavailable && !debugger.IsDebugging)
                {
                    return new { status = unavailable };
                }
                return new { status = "Use the authorized validate operation to run the LLDB version probe.", profile = "Windows x64 / LLVM-MinGW 23.1.6" };
            }
            if (operation == "breakpoints")
            {
                return new { breakpoints = debugger.Breakpoints.All.Where(item => PathNames.Within(item.Path, folder)).Take(count)
                    .Select(point => new { source = Relative(folder, point.Path), line = point.Line + 1, point.Enabled, point.Status, verifiedLine = point.MovedBy is int shift ? point.Line + shift + 1 : (int?)null }).ToArray() };
            }
            if (debugger.Session is not { Engine: DebugEngine.Native } active || sessionId is not null && active.NativeSessionId != sessionId)
            {
                return new { status = "There is no matching native debug session." };
            }
            if (operation == "wait")
            {
                await WaitForNativeStopAsync(active, stop == debugger.NativeInspectionGeneration, TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 1, 60)), cancel);
                return StillCurrent() ? NativeState(active, debugger) : new { status = "The active debug session changed." };
            }
            if (operation == "state")
            {
                return NativeState(active, debugger);
            }
            if (active.State != DebugState.Paused || stop is int generation && debugger.NativeInspectionGeneration != generation)
            {
                return new { status = "Request a current paused debugger state before inspecting handles." };
            }
            if (reference is not null && operation is "frames" or "scopes" or "variables" && (sessionId is null || stop is null))
            {
                return new { status = "Supply the sessionId and stop that own this debugger reference." };
            }
            int current = debugger.NativeInspectionGeneration;
            int? frameId = debugger.SelectedNativeFrame?.Id;
            object result;
            try
            {
                result = operation switch
                {
                    "threads" => new { threads = (await active.ThreadsAsync().WaitAsync(cancel)).Take(count).Select(thread => new
                        { thread.Id, thread.Name, stopped = active.AllThreadsStopped == true || active.StoppedThread == thread.Id ? true : (bool?)null }).ToArray() },
                    "frames" when (reference ?? active.StoppedThread) is int thread => new
                        { frames = (await active.StackTraceAsync(thread).WaitAsync(cancel)).Take(count).Select(frame => NativeFrame(frame, active, folder)).ToArray() },
                    "scopes" when (reference ?? frameId) is int frame => new
                        { scopes = (await active.ScopesAsync(frame).WaitAsync(cancel)).Where(scope => scope.Name != "Registers").Take(count).ToArray() },
                    "variables" when reference is int variables and > 0 => new
                        { variables = (await active.VariablesAsync(variables).WaitAsync(cancel)).Take(count).Select(value => new
                            { value.Name, value.Type, value = VariableNode.Cut(value.Value), reference = value.VariablesReference }).ToArray() },
                    "module" => new { module = NativeModules(active, path is not null ? Resolve(path) : debugger.SelectedNativeFrame?.Source?.Path, count) },
                    _ => new { status = "Choose a supported inspection and a current debugger reference." },
                };
            }
            catch (DapException)
            {
                return new { status = "LLDB could not inspect that reference. Read state again before retrying." };
            }
            return StillCurrent() && active.State == DebugState.Paused && current == debugger.NativeInspectionGeneration
                ? result : new { status = "The stopped frame changed. Request current debugger references." };

            bool StillCurrent() => Session.Debug == debugger && debugger.Session == active && NativeTrusted(out _);
        }, projects: false);

    [McpServerTool(Name = McpTools.NativeDebugControl, Title = "Control the native debugger", Destructive = true), Description(
        "Act on Pitstop's trusted native debugger. Operations: validate, start, set_breakpoint, remove_breakpoint, continue, pause, step_into, step_over, step_out, stop, select_thread, select_frame, evaluate. Validate executes the configured LLDB version probe. Start refuses an existing session. Evaluate explicitly runs an expression in the selected frame and may run debuggee code. Use sessionId and stop from native_debug_inspect for frame handles.")]
    public Task<object> NativeDebugControl(string operation, string? configuration = null, string? sessionId = null,
        int? stop = null, int? reference = null, string? path = null, int? line = null, string? condition = null,
        string? hitCondition = null, string? logMessage = null, string? expression = null, CancellationToken cancel = default) =>
        OnUi<object>(cancel, async () =>
        {
            if (!NativeTrusted(out string folder))
            {
                return new { status = "Open a trusted Xmake workspace." };
            }
            DebugController debugger = Session.Debug;
            if (operation == "validate")
            {
                try
                {
                    LldbInstallation installation = await Builds.FindNativeDebuggerAsync(folder, cancel);
                    return NativeTrusted(out string current) && PathNames.Same(folder, current)
                        ? new { status = "available", adapter = installation.AdapterVersion.ToString(), lldb = installation.LldbVersion.ToString() }
                        : new { status = "The workspace changed during debugger validation." };
                }
                catch (NativeDebugException error)
                {
                    return new { status = error.Message };
                }
            }
            if (operation == "start")
            {
                string? issue = await Builds.StartNativeConfigurationAsync(configuration, cancel);
                return issue is null && Session.Debug == debugger && debugger.Session is { Engine: DebugEngine.Native } opened
                    ? NativeState(opened, debugger) : new { status = issue ?? "The workspace changed during launch." };
            }
            if (operation is "set_breakpoint" or "remove_breakpoint")
            {
                if (path is null || line is not int number || number <= 0 || Resolve(path) is not { } source || !PathNames.Within(source, folder))
                {
                    return new { status = "Supply a source path in the workspace and a one-based line." };
                }
                LineBreakpoint? point = debugger.Breakpoints.In(source).FirstOrDefault(item => item.Line == number - 1 && !item.OnPart);
                if (operation == "remove_breakpoint")
                {
                    if (point is not null)
                    {
                        debugger.Breakpoints.Remove(point);
                    }
                }
                else
                {
                    if (point is null)
                    {
                        debugger.Breakpoints.Set(source, number - 1);
                        point = debugger.Breakpoints.In(source).First(item => item.Line == number - 1 && !item.OnPart);
                    }
                    debugger.Breakpoints.Update(point, true, logMessage is null, condition, hitCondition, logMessage);
                }
                if (debugger.Session is { Engine: DebugEngine.Native } activeDebug)
                {
                    await activeDebug.BreakpointsAnswered.WaitAsync(cancel);
                }
                return new { status = operation == "remove_breakpoint" ? "removed" : "set", verification = point?.Status, line = number };
            }
            if (debugger.Session is not { Engine: DebugEngine.Native, State: not DebugState.Ended } active || sessionId is not null && active.NativeSessionId != sessionId)
            {
                return new { status = "There is no matching native debug session." };
            }
            if (stop is int generation && generation != debugger.NativeInspectionGeneration)
            {
                return new { status = "The stopped context changed. Read state again." };
            }
            try
            {
                if (operation == "evaluate" && active.State == DebugState.Paused && debugger.SelectedNativeFrame is { } frame
                    && !string.IsNullOrWhiteSpace(expression))
                {
                    int current = debugger.NativeInspectionGeneration;
                    EvaluateResponse value = await active.EvaluateAsync(expression, frame.Id, EvaluateContexts.Watch).WaitAsync(cancel);
                    return Session.Debug == debugger && debugger.Session == active && current == debugger.NativeInspectionGeneration
                        ? new { status = "evaluated", value = VariableNode.Cut(value.Result), type = value.Type, reference = value.VariablesReference }
                        : new { status = "The selected frame changed." };
                }
                Task? request = operation switch
                {
                    "continue" when active.State == DebugState.Paused => active.ContinueAsync(),
                    "pause" when active.State == DebugState.Running => active.PauseAsync(),
                    "step_into" when active.State == DebugState.Paused => active.StepIntoAsync(),
                    "step_over" when active.State == DebugState.Paused => active.StepOverAsync(),
                    "step_out" when active.State == DebugState.Paused => active.StepOutAsync(),
                    "stop" => active.StopAsync(),
                    "select_thread" when active.State == DebugState.Paused && reference is int thread and > 0 => debugger.SelectNativeThreadAsync(thread),
                    "select_frame" when active.State == DebugState.Paused && reference is int pickedFrame and > 0 => debugger.SelectNativeFrameAsync(pickedFrame),
                    _ => null,
                };
                if (request is null)
                {
                    return new { status = "That action is unavailable in the current native debugger state." };
                }
                await request.WaitAsync(cancel);
                return Session.Debug == debugger && debugger.Session == active ? NativeState(active, debugger) : new { status = "The active session changed." };
            }
            catch (DapException)
            {
                return new { status = "LLDB rejected the request for the current context. Check the expression or refresh debugger state." };
            }
        }, projects: false);

    private bool NativeTrusted(out string folder)
    {
        folder = Session.Folder ?? string.Empty;
        return folder.Length > 0 && Session.Trusted && WorkspaceTrust.Trusts(folder) && Session.Xmake?.IsXmake == true;
    }

    private static object NativeState(DebugSession session, DebugController debugger) => new
    {
        sessionId = session.NativeSessionId, state = session.State.ToString(), stop = debugger.NativeInspectionGeneration, session.StopReason,
        session.StoppedThread, session.AllThreadsStopped, frame = debugger.SelectedNativeFrame?.Id, session.ExitCode, session.Failure,
        target = session.NativePlan?.Target, configuration = session.NativeWorkspace?.Configuration.Mode, session.Attached,
    };

    private static object NativeFrame(DapStackFrame frame, DebugSession session, string folder) => new
    {
        frame.Id, frame.Name, source = frame.Source?.Path is { } path && PathNames.Within(path, folder) ? Relative(folder, path) : frame.Source?.Name,
        frame.Line, frame.Column, module = NativeModules(session, frame.Source?.Path, 10),
    };

    private static object? NativeModules(DebugSession session, string? path, int count) => session.ModuleContext(path) is { } context ? new
    {
        candidates = context.Providers.Take(count).Select(provider => new { provider.Name, provider.Kind, target = provider.Unit.Context.Target }).ToArray(),
        contexts = context.Units.Take(count).Select(unit => unit.Context.Target).ToArray(),
        ambiguous = context.Units.Length > 1 || context.Providers.Length > 1,
    } : null;

    private static async Task WaitForNativeStopAsync(DebugSession session, bool nextStop, TimeSpan limit, CancellationToken cancel)
    {
        TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int previous = session.Stops;
        void Changed(object? sender, EventArgs args)
        {
            if (session.State == DebugState.Ended || session.State == DebugState.Paused && (!nextStop || previous != session.Stops))
            {
                reached.TrySetResult();
            }
        }
        session.StateChanged += Changed;
        try
        {
            if (session.State != DebugState.Ended && (session.State != DebugState.Paused || nextStop))
            {
                Changed(null, EventArgs.Empty);
                await Task.WhenAny(reached.Task, Task.Delay(limit, cancel));
                cancel.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            session.StateChanged -= Changed;
        }
    }
}

using System.Text.Json;
using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.Modules;
using Pitstop.NativeDebug;
using Pitstop.ProjectSystem;

namespace Pitstop.App.Debugging;

public sealed partial class DebugSession
{
    private readonly NativeDebugSession? native;
    private readonly INativeWorkspace? nativeWorkspace;
    private readonly ModuleWorkspace? nativeModules;
    private Task? nativePump;
    private string? nativeFunctionsSent;
    private bool? nativeCppThrowSent;
    private readonly Dictionary<int, NativeFunctionBreakpoint> nativeFunctionsById = [];
    private readonly Dictionary<string, int> nativeBreakpointRequests = new(PathNames.Comparer);

    public bool? AllThreadsStopped { get; private set; }
    public DapCapabilities? NativeCapabilities => native?.Capabilities;
    public NativeLaunchPlan? NativePlan { get; private init; }
    internal string? NativeSessionId { get; private init; }
    internal INativeWorkspace? NativeWorkspace => nativeWorkspace;

    private DebugSession(NativeDebugSession native, INativeWorkspace workspace, ModuleWorkspace? modules,
        BreakpointStore breakpoints, BuiltLines lines, string program, bool attached)
    {
        this.native = native;
        nativeWorkspace = workspace;
        nativeModules = modules;
        this.breakpoints = breakpoints;
        this.lines = lines;
        columns = (_, _, _, _) => null;
        errorsHeard = Task.CompletedTask;
        Engine = DebugEngine.Native;
        Program = program;
        Attached = attached;
        DataBreakpointsUnavailable = "Data breakpoints are not available in the integrated native debugger.";
        breakpoints.Join(this);
        breakpoints.Changed += OnBreakpointsChanged;
        breakpoints.NativeBreakpointsChanged += OnNativeBreakpointsChanged;
    }

    internal static async Task<DebugSession> StartNativeAsync(LldbInstallation installation, INativeWorkspace workspace,
        ModuleWorkspace? modules, string safeDirectory, NativeLaunchPlan? plan, int? pid, BreakpointStore breakpoints,
        BuiltLines lines, Action<OutputEvent> output, Action<DebugSession> starting, CancellationToken cancel,
        Func<RunInTerminalArguments, CancellationToken, Task<NativeDebugTerminalLaunch>>? terminal = null)
    {
        NativeDebugSession adapter = await NativeDebugSession.StartAsync(installation, workspace.Root, safeDirectory, cancel, terminal: terminal);
        DebugSession session = new(adapter, workspace, modules, breakpoints, lines, plan?.Executable ?? $"Process {pid}", pid is not null)
        {
            NativePlan = plan,
            NativeSessionId = Guid.NewGuid().ToString("N"),
        };
        try
        {
            starting(session);
            session.nativePump = session.ReadNativeEventsAsync(output);
            IdeErrors.Observe(session.nativePump);
            if (pid is int processId)
            {
                session.processStarted.TrySetResult(processId);
                await adapter.AttachAsync(processId, Configure, cancel);
            }
            else
            {
                await adapter.LaunchAsync(plan!, Configure, cancel: cancel);
            }
            if (session.State == DebugState.Starting)
            {
                session.SetState(DebugState.Running);
                session.Sending(session.SendChangedAsync());
            }
            return session;
        }
        catch
        {
            await session.StopNativeAsync();
            throw;
        }

        Task Configure(NativeDebugSession _, CancellationToken stop) => UiThread.InvokeAsync(async () =>
        {
            stop.ThrowIfCancellationRequested();
            await session.SendChangedAsync();
            stop.ThrowIfCancellationRequested();
        });
    }

    private async Task ReadNativeEventsAsync(Action<OutputEvent> output)
    {
        try
        {
            while (State != DebugState.Ended)
            {
                NativeDebugEvent message = await native!.ReadEventAsync(waitIndefinitely: true);
                if (disconnected is not null)
                {
                    break;
                }
                if (message.Name == DapEvents.Output)
                {
                    if (message.Body.Deserialize<OutputEvent>(DapConnection.Options) is { } line)
                    {
                        output(line);
                        Heard?.Invoke(line);
                    }
                }
                else
                {
                    OnEvent(message.Name, new JsonElementCopy(message.Body));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal or workspace revocation retired the adapter.
        }
        catch (NativeDebugException error)
        {
            if (!programEnded && disconnected is null)
            {
                Failure = error.Message;
            }
        }
        finally
        {
            await StopNativeAsync();
        }
    }

    private async Task StopNativeAsync()
    {
        if (State == DebugState.Ended)
        {
            return;
        }
        Detached = Attached;
        endAsked = true;
        ending.TrySetResult();
        try
        {
            await (disconnected ??= native!.DisposeAsync().AsTask());
        }
        catch (NativeDebugException error)
        {
            Failure ??= error.Message;
        }
        finally
        {
            closed = true;
            adapterEnded.TrySetResult();
            Ended(ExitCode);
        }
    }

    private Task<T> AskAsync<T>(string command, object arguments) =>
        native is null ? connection.RequestAsync<T>(command, arguments) : native.RequestAsync<T>(command, arguments);

    private Task AskAsync(string command, object arguments) =>
        native is null ? connection.RequestAsync(command, arguments) : native.RequestAsync<JsonElement>(command, arguments);

    internal async Task<IReadOnlyList<DapVariable>> NativeVariablePageAsync(int reference, int start)
    {
        VariablesResponse result = await AskAsync<VariablesResponse>(DapCommands.Variables,
            new VariablesArguments(reference, Start: start, Count: VariablesTree.PageSize));
        return result.Variables;
    }

    private bool NativeTakes(string path) => nativeWorkspace is { } workspace &&
        (workspace.Targets.Any(target => target.SourceFiles.Contains(path, PathNames.Comparer) || target.HeaderFiles.Contains(path, PathNames.Comparer))
            || PathNames.IsUnder(path, workspace.Root) && Languages.Language.ForPath(path) is var language
                && (language == Languages.Language.C || language == Languages.Language.Cpp));

    private string? WhyNotNativeBreakpoint(LineBreakpoint point) => point.Condition is not null && NativeCapabilities?.SupportsConditionalBreakpoints != true
        ? "This LLDB adapter does not advertise conditional breakpoints."
        : point.HitCondition is not null && NativeCapabilities?.SupportsHitConditionalBreakpoints != true
            ? "This LLDB adapter does not advertise hit-count breakpoints."
        : (point.LogMessage is not null || !point.Suspend) && NativeCapabilities?.SupportsLogPoints != true
            ? "This LLDB adapter does not advertise logpoints."
        : null;

    private void OnNativeBreakpointsChanged(object? sender, EventArgs args)
    {
        if (disconnected is null && State is DebugState.Running or DebugState.Paused)
        {
            Sending(SendNativeBreakpointsAsync());
        }
    }

    private async Task SendNativeBreakpointsAsync()
    {
        List<NativeFunctionBreakpoint> functions = breakpoints.Muted ? [] : [.. breakpoints.NativeFunctions.Where(point => point.Enabled)];
        string signature = Signatures.Of([.. functions.Select(point => point.Name)]);
        if (NativeCapabilities?.SupportsFunctionBreakpoints == true && signature != nativeFunctionsSent)
        {
            nativeFunctionsSent = signature;
            SetBreakpointsResponse result = await AskAsync<SetBreakpointsResponse>(DapCommands.SetFunctionBreakpoints,
                new SetFunctionBreakpointsArguments([.. functions.Select(point => new DapFunctionBreakpoint(point.Name))]));
            if (disconnected is null && nativeFunctionsSent == signature)
            {
                nativeFunctionsById.Clear();
                for (int index = 0; index < Math.Min(functions.Count, result.Breakpoints.Count); index++)
                {
                    DapBreakpoint reply = result.Breakpoints[index];
                    NativeFunctionBreakpoint point = functions[index];
                    (BreakpointStatus status, string? message, _) = Status(reply);
                    point.Tell(this, status, message);
                    if (reply.Id is int id)
                    {
                        nativeFunctionsById[id] = point;
                    }
                }
            }
        }

        bool cppThrow = breakpoints.NativeCppThrow.Enabled && !breakpoints.Muted;
        if (nativeCppThrowSent != cppThrow && disconnected is null
            && JsonMembers.Items(native!.AdvertisedCapabilities, "exceptionBreakpointFilters")
                .Any(item => JsonMembers.Text(item, "filter") == NativeCppThrowBreakpoint.Filter))
        {
            nativeCppThrowSent = cppThrow;
            await AskAsync(DapCommands.SetExceptionBreakpoints, new SetExceptionBreakpointsArguments(cppThrow ? [NativeCppThrowBreakpoint.Filter] : []));
        }
    }

    public DebugModuleContext? ModuleContext(string? source) =>
        nativeModules is { } modules && nativeWorkspace is { } workspace ? DebugModuleContext.For(modules, workspace, source) : null;
}

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.ProjectSystem;

namespace Pitstop.NativeDebug;

public sealed record NativeDebugEvent(string Name, JsonElement Body);
public sealed record NativeDebugTerminalLaunch(RunInTerminalResponse Response, Func<ValueTask> End);
public sealed record LldbStdio(string? Input, string? Output, string? Error);

/// <summary>One LLDB adapter and its DAP conversation. Cancellation retires the entire session.</summary>
public sealed class NativeDebugSession : IAsyncDisposable
{
    internal static readonly string[] AdapterArguments =
        ["--no-lldbinit", "--pre-init-command", "settings set target.load-script-from-symbol-file false", "--repl-mode", "variable"];
    private static readonly TimeSpan DisconnectLimit = TimeSpan.FromSeconds(3);

    private readonly Process process;
    private readonly int adapterId;
    private readonly DapConnection dap;
    private readonly Task adapterErrors;
    private readonly CancellationTokenSource lifetime;
    private readonly CancellationToken active;
    private readonly string scope;
    private readonly TimeSpan requestLimit;
    private readonly Lock stopping = new();
    private readonly Channel<NativeDebugEvent> events = Channel.CreateBounded<NativeDebugEvent>(1024);
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? ending;
    private NativeDebugTerminalLaunch? terminalLaunch;
    private Task? terminalEnding;
    private bool adapterClosed;
    private readonly bool hasTerminal;
    private int terminalRequested;
    private bool launched;
    private int configured;

    private NativeDebugSession(Process process, string scope, TimeSpan requestLimit, CancellationToken cancel,
        Action<NativeDebugPacket>? transcript, Func<RunInTerminalArguments, CancellationToken, Task<NativeDebugTerminalLaunch>>? terminal)
    {
        this.process = process;
        adapterId = process.Id;
        this.scope = scope;
        this.requestLimit = requestLimit;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        active = lifetime.Token;
        hasTerminal = terminal is not null;
        adapterErrors = ChildProcesses.Hear(process, errors: _ => { }, longest: 8192);
        dap = new(ChildProcesses.OutputStream(process), ChildProcesses.Input(process).BaseStream)
        {
            UseGlobalTrace = false,
            MessageTrace = transcript is null ? null : line => transcript(NativeDebugPacket.From(line)),
        };
        if (terminal is not null)
        {
            dap.Handle<RunInTerminalArguments, RunInTerminalResponse>(DapCommands.RunInTerminal,
                request => TerminalAsync(terminal, request));
        }
        dap.EventReceived += OnEvent;
        WorkspaceTrust.Changed += TrustChanged;
        TrustChanged(null, EventArgs.Empty);
        dap.Start();
        UnawaitedWork.Observe(EndWhenCanceledAsync());
        UnawaitedWork.Observe(EndEventsAsync());
    }

    public DapCapabilities Capabilities { get; private set; } = new();
    public JsonElement AdvertisedCapabilities { get; private set; }
    public int AdapterId => adapterId;
    public Task Completion => dap.Completion;

    public static async Task<NativeDebugSession> StartAsync(LldbInstallation installation, string workspace, string safeDirectory,
        CancellationToken cancel = default, Action<NativeDebugPacket>? transcript = null,
        Func<RunInTerminalArguments, CancellationToken, Task<NativeDebugTerminalLaunch>>? terminal = null,
        TimeSpan? requestLimit = null)
    {
        WorkspaceTrust.Check(workspace);
        cancel.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(safeDirectory) || !Directory.Exists(safeDirectory))
        {
            throw new NativeDebugException(NativeDebugFailure.Initialization, "Choose an existing, application-controlled working directory for LLDB.");
        }
        ProcessStartInfo command = ChildProcesses.Command(installation.Adapter, AdapterArguments, safeDirectory);
        command.RedirectStandardInput = command.RedirectStandardOutput = command.RedirectStandardError = true;
        Process process;
        try
        {
            process = await ChildProcesses.StartAsync(command).ConfigureAwait(false);
            ChildProcesses.HoldFamily(process);
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            throw new NativeDebugException(NativeDebugFailure.Initialization, "LLDB did not start. Check lldb-dap and its runtime libraries.", error);
        }
        if (OperatingSystem.IsWindows() && !ChildProcesses.Leaves(process.Id))
        {
            ChildProcesses.Kill(process);
            process.Dispose();
            throw new NativeDebugException(NativeDebugFailure.Initialization, "Windows did not grant a process job for LLDB. Check whether the host restricts process jobs.");
        }
        NativeDebugSession session = new(process, workspace, requestLimit ?? TimeSpan.FromSeconds(20), cancel, transcript, terminal);
        bool ready = false;
        try
        {
            session.AdvertisedCapabilities = await session.RequestAsync<JsonElement>(DapCommands.Initialize, new
            {
                adapterID = "lldb-dap", clientID = "pitstop", clientName = "Pitstop", pathFormat = "path",
                linesStartAt1 = true, columnsStartAt1 = true, supportsVariableType = true,
                supportsVariablePaging = true, supportsRunInTerminalRequest = terminal is not null,
            }, cancel).ConfigureAwait(false);
            session.Capabilities = session.AdvertisedCapabilities.Deserialize<DapCapabilities>(DapConnection.Options)
                ?? throw new JsonException("LLDB did not return capabilities.");
            ready = true;
            return session;
        }
        catch (Exception error) when (error is DapException or JsonException or NativeDebugException)
        {
            throw new NativeDebugException(NativeDebugFailure.Initialization, "LLDB did not initialize over DAP. Check the adapter and its runtime.", error);
        }
        finally
        {
            if (!ready)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task LaunchAsync(NativeLaunchPlan plan, Func<NativeDebugSession, CancellationToken, Task>? configure = null,
        Func<CancellationToken, Task>? build = null, CancellationToken cancel = default, LldbStdio? stdio = null)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(active, cancel);
        WorkspaceTrust.Check(scope);
        if (!PathNames.Same(scope, plan.Workspace.Root))
        {
            throw new NativeDebugException(NativeDebugFailure.InvalidLaunch, "The launch plan belongs to a different workspace.");
        }
        if (plan.Console != NativeConsole.Internal && !hasTerminal)
        {
            throw new NativeDebugException(NativeDebugFailure.InvalidLaunch, "Select the internal console or provide an owned terminal launcher.");
        }
        if (plan.BuildBeforeDebug)
        {
            if (build is null)
            {
                throw new NativeDebugException(NativeDebugFailure.Build, "Provide the project system's build operation or turn off build before debug.");
            }
            try
            {
                await build(linked.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new NativeDebugException(NativeDebugFailure.Build, "The native build failed before LLDB launch. Check the build output.", error);
            }
        }
        WorkspaceTrust.Check(plan.Executable);
        WorkspaceTrust.Check(plan.WorkingDirectory);
        if (!Path.IsPathFullyQualified(plan.Executable) || !File.Exists(plan.Executable) || !Directory.Exists(plan.WorkingDirectory))
        {
            throw new NativeDebugException(NativeDebugFailure.InvalidLaunch,
                "The evaluated executable or working directory is unavailable. Build the selected target and check the working directory.");
        }
        Architecture? architecture = LldbDiscovery.ReadArchitecture(plan.Executable);
        if (architecture is { } target && target != RuntimeInformation.OSArchitecture)
        {
            throw new NativeDebugException(NativeDebugFailure.Architecture, $"The local debug profile does not support the target architecture {target}.");
        }
        string console = plan.Console switch
        {
            NativeConsole.IntegratedTerminal => "integratedTerminal",
            NativeConsole.ExternalTerminal => "externalTerminal",
            _ => "internalConsole",
        };
        await ConfigureAsync(DapCommands.Launch, new
        {
            program = plan.Executable, cwd = plan.WorkingDirectory, args = plan.Arguments, env = plan.Environment,
            stopOnEntry = plan.StopOnEntry, console,
            stdio = stdio is null ? null : new[] { stdio.Input, stdio.Output, stdio.Error },
            sourceMap = plan.SourceMaps.IsDefaultOrEmpty ? null
                : plan.SourceMaps.Select(map => new[] { map.BuildPath, map.WorkspacePath }).ToArray(),
        }, configure, linked.Token).ConfigureAwait(false);
    }

    public Task AttachAsync(int processId, Func<NativeDebugSession, CancellationToken, Task>? configure = null,
        CancellationToken cancel = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        return ConfigureAsync(DapCommands.Attach, new { pid = processId, stopOnEntry = true }, configure, cancel);
    }

    private async Task ConfigureAsync(string command, object arguments, Func<NativeDebugSession, CancellationToken, Task>? configure,
        CancellationToken cancel)
    {
        WorkspaceTrust.Check(scope);
        if (Interlocked.Exchange(ref configured, 1) != 0)
        {
            throw new InvalidOperationException("This adapter already has a launch or attach request.");
        }
        launched = command == DapCommands.Launch;
        using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(active, cancel);
        bound.CancelAfter(requestLimit);
        bound.Token.ThrowIfCancellationRequested();
        Task starting = dap.RequestAsync(command, arguments, bound.Token);
        bool ready = false;
        try
        {
            Task first = await Task.WhenAny(starting, initialized.Task).WaitAsync(bound.Token).ConfigureAwait(false);
            if (first == starting)
            {
                await starting.ConfigureAwait(false);
            }
            await initialized.Task.WaitAsync(bound.Token).ConfigureAwait(false);
            if (configure is not null)
            {
                await configure(this, bound.Token).ConfigureAwait(false);
            }
            if (Capabilities.SupportsConfigurationDoneRequest == true)
            {
                await dap.RequestAsync(DapCommands.ConfigurationDone, null, bound.Token).ConfigureAwait(false);
            }
            await starting.ConfigureAwait(false);
            bound.Token.ThrowIfCancellationRequested();
            ready = true;
        }
        catch (OperationCanceledException error) when (!cancel.IsCancellationRequested && !active.IsCancellationRequested)
        {
            throw new NativeDebugException(NativeDebugFailure.Timeout, $"LLDB did not finish {command} and configuration in time.", error);
        }
        catch (DapException error) when (dap.Completion.IsCompleted)
        {
            throw new NativeDebugException(NativeDebugFailure.Adapter, $"LLDB closed its connection during {command}. Check whether the adapter crashed.", error);
        }
        finally
        {
            if (!ready)
            {
                await bound.CancelAsync().ConfigureAwait(false);
                UnawaitedWork.Dismiss(starting);
                await DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<T> RequestAsync<T>(string command, object? arguments = null, CancellationToken cancel = default)
    {
        using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(active, cancel);
        bound.CancelAfter(requestLimit);
        bound.Token.ThrowIfCancellationRequested();
        try
        {
            T response = await dap.RequestAsync<T>(command, arguments, bound.Token).ConfigureAwait(false);
            bound.Token.ThrowIfCancellationRequested();
            return response;
        }
        catch (OperationCanceledException error) when (!cancel.IsCancellationRequested && !active.IsCancellationRequested)
        {
            throw new NativeDebugException(NativeDebugFailure.Timeout, $"LLDB did not answer {command} in time.", error);
        }
        catch (DapException error) when (dap.Completion.IsCompleted)
        {
            throw new NativeDebugException(NativeDebugFailure.Adapter, $"LLDB closed its connection during {command}. Check whether the adapter crashed.", error);
        }
    }

    public async Task<NativeDebugEvent> ReadEventAsync(CancellationToken cancel = default, bool waitIndefinitely = false)
    {
        using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(active, cancel);
        if (!waitIndefinitely)
        {
            bound.CancelAfter(requestLimit);
        }
        try
        {
            bound.Token.ThrowIfCancellationRequested();
            NativeDebugEvent next = await events.Reader.ReadAsync(bound.Token).ConfigureAwait(false);
            bound.Token.ThrowIfCancellationRequested();
            return next;
        }
        catch (OperationCanceledException error) when (!cancel.IsCancellationRequested && !active.IsCancellationRequested)
        {
            throw new NativeDebugException(NativeDebugFailure.Timeout, "LLDB did not deliver the expected debugger event.", error);
        }
        catch (ChannelClosedException error) when (error.InnerException is NativeDebugException failure)
        {
            throw new NativeDebugException(failure.Failure, failure.Message, failure);
        }
    }

    private void OnEvent(string name, JsonElement body)
    {
        if (active.IsCancellationRequested)
        {
            return;
        }
        if (name == DapEvents.Initialized)
        {
            initialized.TrySetResult();
        }
        if (!events.Writer.TryWrite(new(name, body)))
        {
            _ = lifetime.CancelAsync();
        }
    }

    private async Task<RunInTerminalResponse> TerminalAsync(
        Func<RunInTerminalArguments, CancellationToken, Task<NativeDebugTerminalLaunch>> terminal, RunInTerminalArguments request)
    {
        WorkspaceTrust.Check(scope);
        active.ThrowIfCancellationRequested();
        if (!launched || request.Args is not [string tool, ..] || !Path.IsPathFullyQualified(tool)
            || !PathNames.Same(PathNames.Canonical(tool), PathNames.Canonical(process.StartInfo.FileName))
            || !request.Args.Contains("--launch-target", StringComparer.Ordinal) || Interlocked.Exchange(ref terminalRequested, 1) != 0)
        {
            throw new InvalidOperationException("LLDB requested an unexpected terminal program. Enable the internal console or check the adapter.");
        }
        NativeDebugTerminalLaunch started = await terminal(request, active).ConfigureAwait(false);
        lock (stopping)
        {
            if (!active.IsCancellationRequested && !adapterClosed)
            {
                terminalLaunch = started;
                return started.Response;
            }
        }
        await started.End().ConfigureAwait(false);
        throw new OperationCanceledException(active);
    }

    private async Task EndEventsAsync()
    {
        await dap.EventsCompletion.ConfigureAwait(false);
        lock (stopping)
        {
            adapterClosed = true;
            if (!active.IsCancellationRequested)
            {
                ChildProcesses.Kill(process);
                UnawaitedWork.Observe(EndTerminal());
            }
        }
        events.Writer.TryComplete(new NativeDebugException(NativeDebugFailure.Adapter, "LLDB closed its event stream. Check whether the adapter crashed."));
    }

    // The terminal can end on adapter EOF or on explicit disposal. Both paths await the same cleanup.
    private Task EndTerminal()
    {
        lock (stopping)
        {
            return terminalEnding ??= terminalLaunch is { } terminal ? EndTerminalAsync(terminal) : Task.CompletedTask;
        }
    }

    private static async Task EndTerminalAsync(NativeDebugTerminalLaunch terminal)
    {
        try
        {
            await terminal.End().AsTask().WaitAsync(DisconnectLimit).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
        {
            throw new NativeDebugException(NativeDebugFailure.Cleanup, "The owned LLDB terminal could not be stopped cleanly.", error);
        }
    }

    private void TrustChanged(object? sender, EventArgs args)
    {
        lock (stopping)
        {
            if (!active.IsCancellationRequested && !WorkspaceTrust.Trusts(scope))
            {
                _ = lifetime.CancelAsync();
            }
        }
    }

    private async Task EndWhenCanceledAsync()
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, active).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (stopping)
        {
            return new(ending ??= EndAsync());
        }
    }

    private async Task EndAsync()
    {
        WorkspaceTrust.Changed -= TrustChanged;
        await lifetime.CancelAsync().ConfigureAwait(false);
        events.Writer.TryComplete();
        using CancellationTokenSource disconnect = new(DisconnectLimit);
        try
        {
            await dap.RequestAsync(DapCommands.Disconnect, new DisconnectArguments(TerminateDebuggee: launched), disconnect.Token)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is DapException or IOException or OperationCanceledException)
        {
        }
        finally
        {
            ChildProcesses.Kill(process);
            try
            {
                await dap.DisposeAsync().ConfigureAwait(false);
                await process.WaitForExitAsync().WaitAsync(DisconnectLimit).ConfigureAwait(false);
                bool drained = await ChildProcesses.OutputEndsAsync(process, adapterErrors).ConfigureAwait(false);
                if (!drained)
                {
                    throw new NativeDebugException(NativeDebugFailure.Cleanup, "LLDB left its adapter output open after shutdown.");
                }
            }
            catch (TimeoutException error)
            {
                throw new NativeDebugException(NativeDebugFailure.Cleanup, "LLDB did not exit after its process family was stopped.", error);
            }
            finally
            {
                try
                {
                    await EndTerminal().ConfigureAwait(false);
                }
                finally
                {
                    process.Dispose();
                    lifetime.Dispose();
                }
            }
        }
    }
}

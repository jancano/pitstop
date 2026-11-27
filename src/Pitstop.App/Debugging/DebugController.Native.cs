using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.Editor;
using Pitstop.Modules;
using Pitstop.NativeDebug;
using Pitstop.ProjectSystem;
using Pitstop.Text;

namespace Pitstop.App.Debugging;

public sealed partial class DebugController
{
    internal Task<string?> OpenNativeAsync(string name, LldbInstallation installation, INativeWorkspace workspace,
        ModuleWorkspace? modules, string safeDirectory, NativeLaunchPlan? plan, int? attach, CancellationToken cancel, string? warning = null)
    {
        bool inTerminal = plan?.Console == NativeConsole.IntegratedTerminal;
        return OpenAsync((built, log, output, starting) =>
        {
            if (warning is not null)
            {
                log(warning);
            }
            return DebugSession.StartNativeAsync(installation, workspace, modules, safeDirectory, plan, attach, Breakpoints, built,
                output, starting, cancel, inTerminal ? OpenTerminal : null);
        }, name, launched: attach is null, inTerminal: inTerminal, stopLetsGo: attach is not null, nativeDebug: true);

        async Task<NativeDebugTerminalLaunch> OpenTerminal(RunInTerminalArguments request, CancellationToken stop)
        {
            return await UiThread.InvokeAsync(async () =>
            {
                stop.ThrowIfCancellationRequested();
                Run.TerminalRun run = await (RunInTerminal ?? throw new InvalidOperationException("The integrated terminal is unavailable."))(name, request);
                if (!run.IsRunning)
                {
                    throw new InvalidOperationException("The integrated terminal could not start LLDB's launch helper.");
                }
                System.Diagnostics.Process owned = System.Diagnostics.Process.GetProcessById(run.ProcessId);
                ChildProcesses.HoldFamily(owned);
                if (!ChildProcesses.Leaves(owned.Id))
                {
                    ChildProcesses.Kill(owned);
                    owned.Dispose();
                    throw new InvalidOperationException("Windows did not grant an ownership job for the native debug terminal.");
                }
                return new NativeDebugTerminalLaunch(new(run.Debuggee), async () =>
                {
                    using (owned)
                    {
                        ChildProcesses.Kill(owned);
                        await owned.WaitForExitAsync().ConfigureAwait(false);
                    }
                });
            });
        }
    }

    private static string NativeRequestProblem(DapException error) => error.Command switch
    {
        DapCommands.Attach => "LLDB could not attach. Check the process still exists and your account can debug it.",
        DapCommands.Launch => "LLDB could not launch the target. Check its executable, working directory and runtime libraries.",
        DapCommands.SetBreakpoints => "LLDB could not bind the requested breakpoints.",
        _ => $"LLDB rejected {error.Command}.",
    };

    private readonly Dictionary<string, DapVariable> nativeValues = new(StringComparer.Ordinal);
    internal DapStackFrame? SelectedNativeFrame => Session is { Engine: DebugEngine.Native, State: DebugState.Paused } ? frame : null;
    internal int NativeInspectionGeneration => shown;

    internal Task SelectNativeThreadAsync(int id) => ChooseThread(id);

    internal async Task<bool> SelectNativeFrameAsync(int id)
    {
        if (view.Frames.FirstOrDefault(candidate => candidate.Id == id) is not { } selected)
        {
            return false;
        }
        view.SelectNativeFrame(selected);
        await ChooseFrame(selected, navigate: true);
        return true;
    }

    public event EventHandler<string>? RevealNativeModule;

    internal string? NativeModuleName(string? source)
    {
        DebugModuleContext? context = Session?.ModuleContext(source);
        return context is { Units.Length: 1, Providers.Length: 1 } ? context.Providers[0].Name : null;
    }

    private void RevealFrameModule(object? sender, DapStackFrame frame)
    {
        if (frame.Source?.Path is { } source && NativeModuleName(source) is not null)
        {
            RevealNativeModule?.Invoke(this, source);
        }
    }

    private async Task ShowNativeVariablesAsync(DebugSession session, DapStackFrame current, int generation)
    {
        List<VariableNode> rows = [];
        Dictionary<string, DapVariable> values = new(StringComparer.Ordinal);
        HashSet<string> ambiguous = new(StringComparer.Ordinal);
        try
        {
            IReadOnlyList<DapScope> scopes = await session.ScopesAsync(current.Id);
            foreach (DapScope scope in scopes)
            {
                if (!Current())
                {
                    return;
                }
                if (!scope.Expensive && scope.Name is "Locals" or "Arguments")
                {
                    IReadOnlyList<DapVariable> variables = await session.VariablesAsync(scope.VariablesReference);
                    foreach (DapVariable value in variables)
                    {
                        if (!values.TryAdd(value.Name, value))
                        {
                            ambiguous.Add(value.Name);
                        }
                    }
                    rows.AddRange(variables.Select(value => VariableNode.From(value with { Unsettable = NoNativeMutation }, 0)));
                    if (variables.Count == VariablesTree.PageSize)
                    {
                        rows.Add(VariablesTree.NextPage(scope.VariablesReference, 0, VariablesTree.PageSize));
                    }
                }
                else if (scope.VariablesReference > 0 && scope.Name != "Registers")
                {
                    rows.Add(new VariableNode(scope.Name, string.Empty, null, scope.VariablesReference, 0, VariableRole.Scope));
                }
            }
        }
        catch (DapException error)
        {
            rows.Add(new VariableNode("Unavailable", NativeRequestProblem(error), null, 0, 0, VariableRole.Error));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (NativeDebugException error)
        {
            rows.Add(new VariableNode("Unavailable", error.Message, null, 0, 0, VariableRole.Error));
        }
        if (!Current())
        {
            return;
        }
        nativeValues.Clear();
        foreach ((string name, DapVariable value) in values)
        {
            if (!ambiguous.Contains(name) && value.PresentationHint?.Lazy != true)
            {
                nativeValues.Add(name, value);
            }
        }
        List<VariableNode> watched = [.. Breakpoints.NativeWatchExpressions.Select(expression => nativeValues.TryGetValue(expression, out DapVariable? value)
            ? new VariableNode(expression, value.Value, value.Type, value.VariablesReference, 0, VariableRole.Watch) { Unsettable = NoNativeMutation }
            : new VariableNode(expression, "Use Evaluate to run an expression explicitly.", null, 0, 0, VariableRole.Watch) { Unsettable = NoNativeMutation })];
        if (lastResult is { } result && result.Stop == generation)
        {
            watched.Add(result.Row);
        }
        await view.Tree.ShowAgain([.. watched, .. rows], Current);
        if (!Current())
        {
            return;
        }
        view.SayMoved(null);
        ShowInlineValues(current, nativeValues.ToDictionary(item => item.Key, item => item.Value.Value, StringComparer.Ordinal));
        LocalsShown();

        bool Current() => generation == shown && Session == session && session.State == DebugState.Paused && frame == current;
    }

    private const string NoNativeMutation = "Native variable mutation has not been verified.";

    private async Task<IReadOnlyList<DapVariable>> NativeMembersAsync(DebugSession session, int reference, int start)
    {
        if (Session != session || session.State != DebugState.Paused)
        {
            return [];
        }
        int generation = shown;
        CancellationToken cancel = stopShown.Token;
        try
        {
            IReadOnlyList<DapVariable> members = await session.NativeVariablePageAsync(reference, start).WaitAsync(cancel);
            return Session == session && session.State == DebugState.Paused && shown == generation
                ? [.. members.Select(value => value with { Unsettable = NoNativeMutation })] : [];
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (NativeDebugException error)
        {
            return [new DapVariable { Name = "Unavailable", Value = error.Message }];
        }
    }

    private CSharp.DebuggerHover? NativeHover(int offset)
    {
        if (Session is not { State: DebugState.Paused } || frame?.Source?.Path is not { } path
            || editors.Active is not { } file || !PathNames.Same(path, file.Path))
        {
            return null;
        }
        TextSnapshot text = file.Text.Snapshot;
        if (offset < 0 || offset >= text.Length || text.GetLineFromOffset(offset) != Current(path, frame.Line - 1) || !Word(text[offset]))
        {
            return null;
        }
        int start = offset;
        int end = offset + 1;
        while (start > 0 && Word(text[start - 1]))
        {
            --start;
        }
        while (end < text.Length && Word(text[end]))
        {
            ++end;
        }
        string name = text.GetText(start, end - start);
        return nativeValues.TryGetValue(name, out DapVariable? value) ? new CSharp.DebuggerHover($"{name} = {VariableNode.Cut(value.Value)}")
        {
            Gone = stopShown.Token,
        } : null;
    }

    private static bool Word(char character) => char.IsAsciiLetterOrDigit(character) || character == '_';

    private static IReadOnlyList<LineAnnotation> NativeInlineValues(TextSnapshot text, int line, IReadOnlyDictionary<string, string> values)
    {
        if (line < 0 || line >= text.LineCount)
        {
            return [];
        }
        LineSpan span = text.GetLine(line);
        string source = text.GetText(span.Start, span.Length);
        List<string> found = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int at = 0; at < source.Length && found.Count < 4;)
        {
            int start = at++;
            if (!Word(source[start]))
            {
                continue;
            }
            while (at < source.Length && Word(source[at]))
            {
                ++at;
            }
            string token = source[start..at];
            if (values.TryGetValue(token, out string? value) && seen.Add(token))
            {
                found.Add($"{token} = {VariableNode.Cut(value)}");
            }
        }
        return found.Count == 0 ? [] : [new LineAnnotation(line, string.Join("    ", found), LineAnnotationLook.Muted)];
    }
}

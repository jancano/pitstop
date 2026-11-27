using System.Text.Json;
using Pitstop.App.CSharp;
using Pitstop.Core;
using Pitstop.Editor;
using Pitstop.Languages;
using Pitstop.Languages.CSharp;
using Pitstop.Lsp;
using Pitstop.Text;
using Pitstop.Cpp;
using Pitstop.Xmake;

namespace Pitstop.App.Workspace;

/// <summary>Connects the open native editor documents to the current trusted Xmake compilation context.</summary>
public sealed class CppLanguageService : IAsyncDisposable
{
    private readonly string root;
    private readonly string logs;
    private readonly XmakeWorkspaceSession xmake;
    private readonly Func<string, string, string, CancellationToken, Task<LanguageSession>> start;
    private readonly Dictionary<OpenDocument, CppEditorDocument> documents = [];
    private readonly Debounce restart;
    private XmakeWorkspace? snapshot;
    private CppCompilation? compilation;
    private LanguageSession? session;
    private CancellationTokenSource? lifetime;
    private Task ending = Task.CompletedTask;
    private Task starting = Task.CompletedTask;
    private Task<PendingServer>? launched;
    private int generation;
    private bool recovered;
    private bool closed;

    internal CppLanguageService(string root, string state, XmakeWorkspaceSession xmake,
        Func<string, string, string, CancellationToken, Task<LanguageSession>>? start)
    {
        this.root = root;
        this.xmake = xmake;
        this.start = start ?? Clangd.StartAsync;
        logs = Path.Combine(state, "logs", "clangd");
        restart = new(TimeSpan.FromMilliseconds(300), StartAsync);
        xmake.Changed += OnWorkspaceChanged;
    }

    public string? State { get; private set; }
    public LanguageServer? Server => session?.Server;
    public event EventHandler? Changed;

    public CppEditorDocument? For(OpenDocument document) => documents.GetValueOrDefault(document);

    internal string? TargetFor(string path) => compilation?.CommandsFor(path).FirstOrDefault()?.Target;

    public string? Label(OpenDocument document)
    {
        if (!xmake.IsXmake)
        {
            return null;
        }
        if (State is { } state)
        {
            return state;
        }
        if (compilation is { } active && session is not null)
        {
            string context = active.Context(document.Path);
            if (active.CommandsFor(document.Path).IsEmpty && context.Length == 0
                && Path.GetExtension(document.Path) is not (".h" or ".hpp" or ".hh" or ".hxx"))
            {
                return "clangd · no Xmake command for this file";
            }
            return context.Length == 0 ? "clangd" : $"clangd · {context}";
        }
        return null;
    }

    internal void Attach(OpenDocument document)
    {
        if (document.Language.Service != LanguageService.Clangd || document.IsBinary || !PathNames.Within(document.Path, root) || documents.ContainsKey(document))
        {
            return;
        }
        CppEditorDocument bound = new(document, OnSaved);
        documents.Add(document, bound);
        if (session is { } ready && compilation is { } context && WorkspaceTrust.Trusts(root))
        {
            bound.Connect(ready, context);
        }
        else if (documents.Count == 1)
        {
            FollowWorkspace();
        }
    }

    internal void Detach(OpenDocument document)
    {
        if (documents.Remove(document, out CppEditorDocument? bound))
        {
            bound.Dispose();
        }
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => FollowWorkspace();

    private void FollowWorkspace()
    {
        if (closed)
        {
            return;
        }
        if (!WorkspaceTrust.Trusts(root) || !xmake.IsXmake || xmake.IsEvaluating || xmake.Snapshot is not { } current)
        {
            snapshot = null;
            Invalidate();
            Say(!xmake.IsXmake ? null : !WorkspaceTrust.Trusts(root) ? "Trust this folder to start clangd"
                : xmake.IsEvaluating ? "Waiting for Xmake configuration" : xmake.Problem ?? "Waiting for Xmake targets");
            return;
        }
        if (!ReferenceEquals(current, snapshot))
        {
            snapshot = current;
            recovered = false;
            Invalidate();
        }
        if (documents.Count > 0 && session is null && lifetime is null && !restart.IsWaiting)
        {
            if (current.CompilationDatabases is null)
            {
                Say(current.CompilationDatabaseProblem ?? "Xmake has no semantic compilation database");
                return;
            }
            Say("Starting clangd");
            restart.Restart();
        }
    }

    private void OnSaved(OpenDocument document)
    {
        if (compilation?.IsModuleInput(document.Path) == true)
        {
            RefreshModules();
        }
    }

    internal void FilesChanged(FolderChanges changes)
    {
        if (compilation is { } active && changes.Written.Any(active.IsModuleInput))
        {
            RefreshModules();
        }
    }

    private void RefreshModules()
    {
        if (snapshot is not null && documents.Count > 0 && WorkspaceTrust.Trusts(root))
        {
            Invalidate(keepCompilation: true);
            Say("Refreshing C++ modules");
            restart.Restart();
        }
    }

    private void Invalidate(bool keepCompilation = false)
    {
        generation++;
        CancellationTokenSource? previous = lifetime;
        previous?.Cancel();
        if (starting.IsCompleted)
        {
            previous?.Dispose();
        }
        lifetime = null;
        restart.Stop();
        foreach (CppEditorDocument document in documents.Values)
        {
            document.Disconnect();
        }
        if (session is { } prior)
        {
            session = null;
            ending = Task.WhenAll(ending, prior.DisposeAsync().AsTask());
        }
        if (!keepCompilation)
        {
            compilation = null;
        }
    }

    private async Task StartAsync()
    {
        if (closed || snapshot is not { } current || documents.Count == 0 || !WorkspaceTrust.Trusts(root))
        {
            return;
        }
        int revision = generation;
        CancellationTokenSource source = new();
        lifetime = source;
        CancellationToken cancel = source.Token;
        Task prior = ending;
        Task beginning = starting;
        using IDisposable held = BackgroundWork.Scope();
        starting = ConnectAsync();
        await starting;

        async Task ConnectAsync()
        {
            try
            {
                await Task.WhenAll(prior, beginning);
                if (!Current())
                {
                    return;
                }
                CppCompilation context = compilation ?? await BackgroundWork.Run(() => CppCompilation.ReadAsync(current, cancel), cancel);
                Task<PendingServer> launching = BackgroundWork.Unheld(async () => new PendingServer(
                    await start(root, context.Database, logs, cancel).ConfigureAwait(false)), cancel);
                launched = launching;
                PendingServer pending = await launching;
                if (!Current() || !WorkspaceTrust.Trusts(root))
                {
                    await pending.CloseAsync();
                    return;
                }
                LanguageSession? connected = pending.Take();
                if (connected is null)
                {
                    return;
                }
                session = connected;
                connected.Server.RequestLifetime = cancel;
                compilation = context;
                connected.Server.Exited += (_, problem) => UiThread.Post(() => ServerExited(connected, problem));
                if (connected.Server.Ended is { } problem)
                {
                    ServerExited(connected, problem);
                    return;
                }
                foreach (CppEditorDocument document in documents.Values)
                {
                    document.Connect(connected, context);
                }
                Say(null);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
            }
            catch (Exception problem) when (problem is ClangdException or LanguageServerFailed or IOException or JsonException
                or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException)
            {
                if (Current())
                {
                    lifetime = null;
                    Say($"clangd could not start. {problem.Message}");
                    PassengerLog.Write($"clangd in {root}: {problem}");
                }
            }
            finally
            {
                if (lifetime != source)
                {
                    source.Dispose();
                }
            }
        }

        bool Current() => !closed && revision == generation && !cancel.IsCancellationRequested;
    }

    private void ServerExited(LanguageSession failed, string problem)
    {
        if (failed != session || closed)
        {
            return;
        }
        Invalidate(keepCompilation: true);
        PassengerLog.Write($"clangd ended in {root}: {problem}");
        if (!recovered && WorkspaceTrust.Trusts(root))
        {
            recovered = true;
            Say("Recovering clangd");
            restart.Restart();
        }
        else
        {
            Say($"clangd stopped. Reopen the workspace to retry. {problem}");
        }
    }

    private void Say(string? state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        closed = true;
        xmake.Changed -= OnWorkspaceChanged;
        Invalidate();
        foreach (CppEditorDocument document in documents.Values)
        {
            document.Dispose();
        }
        documents.Clear();
        return new(Task.WhenAll(ending, EndLaunchAsync(launched)));
    }

    private static async Task EndLaunchAsync(Task<PendingServer>? launch)
    {
        if (launch is not null)
        {
            await ((Task)launch).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (launch.IsCompletedSuccessfully)
            {
                await launch.Result.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class PendingServer(LanguageSession session)
    {
        private LanguageSession? ready = session;

        public LanguageSession? Take() => Interlocked.Exchange(ref ready, null);

        public ValueTask CloseAsync() => Take()?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}

public sealed class CppEditorDocument : IServerDocument, IDisposable
{
    private readonly OpenDocument document;
    private readonly ServerDiagnostics diagnostics;
    private readonly Action<OpenDocument> saved;
    private readonly Debounce decorations;
    private CancellationTokenSource? colouring;
    private SemanticHighlighter? semantic;
    private IHighlighter? syntax;
    private TextSnapshot lastSaved;

    internal CppEditorDocument(OpenDocument document, Action<OpenDocument> saved)
    {
        this.document = document;
        this.saved = saved;
        diagnostics = new(document, "clangd");
        decorations = new(TimeSpan.FromMilliseconds(180), DecorateAsync);
        lastSaved = document.Text.Saved;
        document.Saved += OnSaved;
        document.Text.Changed += OnChanged;
    }

    public LspDocument? Lsp { get; private set; }
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics.Placed;
    public bool HasDiagnostics { get; private set; }

    internal void Connect(LanguageSession session, CppCompilation context)
    {
        Language language = Language.ForPath(document.Path);
        if (Path.GetExtension(document.Path).Equals(".h", StringComparison.OrdinalIgnoreCase))
        {
            language = context.HeaderLanguage(document.Path, language.Id) == LanguageIds.Cpp ? Language.Cpp : Language.C;
            document.SetLanguage(language);
        }
        LspDocument open = session.Open(document.Path, document.Text, language.IdOf(document.Path));
        Lsp = open;
        open.DiagnosticsPushed += OnPushed;
        OnPushed(open, EventArgs.Empty);
        if (session.Server.Legend is { } legend && document.Highlighter is { } parsed)
        {
            syntax = parsed;
            semantic = new SemanticHighlighter(parsed, open, legend);
            document.Highlighter = semantic;
        }
        decorations.Restart();
    }

    private void OnPushed(object? sender, EventArgs e)
    {
        if (sender is not LspDocument { HasPushed: true } served)
        {
            return;
        }
        (IReadOnlyList<Diagnostic> items, int version) = served.Pushed;
        UiThread.Post(() =>
        {
            if (Lsp == served && version == document.Text.Version)
            {
                HasDiagnostics = true;
                if (diagnostics.Take(items, DiagnosticMarks.Marker) is { } marks)
                {
                    document.Markers = marks;
                }
            }
        });
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        diagnostics.Moved();
        HasDiagnostics = false;
        colouring?.Cancel();
        if (Lsp is not null)
        {
            decorations.Restart();
        }
    }

    private void OnSaved(object? sender, EventArgs e)
    {
        Lsp?.Saved();
        if (lastSaved != document.Text.Saved)
        {
            lastSaved = document.Text.Saved;
            saved(document);
        }
    }

    private async Task DecorateAsync()
    {
        if (Lsp is not { } served)
        {
            return;
        }
        using CancellationTokenSource current = new();
        colouring = current;
        try
        {
            if (served.Supports("inlayHintProvider"))
            {
                (IReadOnlyList<LspInlayHint> hints, int version) = await served.InlayHintsAsync(current.Token);
                if (Lsp == served && version == document.Text.Version && !current.IsCancellationRequested)
                {
                    document.InlayHints = [.. hints.Select(hint => new InlayHint(served.OffsetOf(hint.Position), hint.Label,
                        PadLeft: hint.PaddingLeft, PadRight: hint.PaddingRight))];
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (LanguageServerFailed) { }
        finally
        {
            if (colouring == current)
            {
                colouring = null;
            }
        }
    }

    internal void Disconnect()
    {
        decorations.Stop();
        colouring?.Cancel();
        if (semantic is not null)
        {
            semantic.Dispose();
            if (document.Highlighter == semantic)
            {
                document.Highlighter = syntax;
            }
            semantic = null;
            syntax = null;
        }
        if (Lsp is { } open)
        {
            Lsp = null;
            open.DiagnosticsPushed -= OnPushed;
            open.Dispose();
        }
        HasDiagnostics = false;
        diagnostics.Take([], DiagnosticMarks.Marker);
        document.Markers = [];
        document.InlayHints = [];
    }

    public void Dispose()
    {
        Disconnect();
        document.Saved -= OnSaved;
        document.Text.Changed -= OnChanged;
    }
}

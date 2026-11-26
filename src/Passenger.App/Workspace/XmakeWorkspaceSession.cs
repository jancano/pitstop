using Passenger.Core;
using Passenger.Editor;
using Pitstop.Xmake;

namespace Passenger.App.Workspace;

/// <summary>The Xmake project in this folder, followed on the UI thread and evaluated in the background.</summary>
public sealed class XmakeWorkspaceSession
{
    internal const string TrustRequired = "Trust this folder to read its Xmake targets and configuration.";

    private readonly string root;
    private readonly string projectFile;
    private readonly Func<string, string, CancellationToken, Task<XmakeWorkspace>> evaluate;
    private readonly Debounce refresh;
    private string? scratch;
    private WatchedFile? configuration;
    private CancellationTokenSource? cancel;
    private Task running = Task.CompletedTask;
    private Task<XmakeWorkspace>? worker;
    private int requested;
    private bool closed;
    private Operation? operation;
    private FileStamp? projectStamp;
    private FileStamp? configurationStamp;
    private bool recheckAfterOperation;
    private string refreshReason = "the workspace was refreshed";

    internal XmakeWorkspaceSession(string root, Func<string, string, CancellationToken, Task<XmakeWorkspace>>? evaluate)
    {
        this.root = root;
        projectFile = Path.Combine(root, XmakeService.ProjectFile);
        this.evaluate = evaluate ?? new XmakeService().EvaluateAsync;
        IsXmake = XmakeService.IsWorkspace(root);
        refresh = new(TimeSpan.FromMilliseconds(250), EvaluateAsync);
    }

    public bool IsXmake { get; private set; }
    public bool IsEvaluating { get; private set; }
    public XmakeWorkspace? Snapshot { get; private set; }
    public string? Problem { get; private set; }
    public event EventHandler? Changed;

    internal string? WhyNotOperate => closed ? "The Xmake workspace is closed."
        : !WorkspaceTrust.Trusts(root) ? TrustRequired
        : operation is not null ? "An Xmake operation is active. Stop it or wait for it to finish."
        : IsEvaluating ? "Wait for Xmake to finish evaluating the active configuration."
        : Snapshot is null ? Problem ?? "Open a folder with an evaluated xmake.lua project." : null;

    internal Operation? BeginOperation()
    {
        UiThread.Check();
        if (WhyNotOperate is not null || Snapshot is not { } snapshot)
        {
            return null;
        }
        if (!InputsMatch(snapshot))
        {
            Refresh();
            return null;
        }
        return operation = new(this, snapshot, requested);
    }

    private bool InputsMatch(XmakeWorkspace snapshot) => FileStamp.Of(projectFile) == projectStamp
        && FileStamp.Of(snapshot.Configuration.ConfigurationFile) == configurationStamp;

    internal sealed class Operation(XmakeWorkspaceSession owner, XmakeWorkspace snapshot, int generation) : IDisposable
    {
        private readonly CancellationTokenSource stopped = new();
        internal XmakeWorkspace Snapshot { get; } = snapshot;
        internal CancellationToken Stopping => stopped.Token;
        internal bool IsCurrent => ObsoleteReason is null;
        internal string? ObsoleteReason => owner.closed ? "the workspace closed"
            : !WorkspaceTrust.Trusts(Snapshot.Root) ? "workspace trust was revoked"
            : FileStamp.Of(owner.projectFile) != owner.projectStamp ? "xmake.lua changed"
            : FileStamp.Of(Snapshot.Configuration.ConfigurationFile) != owner.configurationStamp ? "the active Xmake configuration file changed"
            : owner.requested != generation ? owner.refreshReason : null;

        internal void Cancel() => stopped.Cancel();

        public void Dispose()
        {
            stopped.Dispose();
            owner.operation = null;
            if (!owner.closed && owner.IsEvaluating)
            {
                owner.refresh.Restart();
            }
            else if (!owner.closed && owner.Snapshot == Snapshot && (owner.recheckAfterOperation || !owner.InputsMatch(Snapshot)))
            {
                owner.Refresh();
            }
            owner.recheckAfterOperation = false;
        }
    }

    internal void Refresh() => Refresh("the workspace was refreshed");

    private void Refresh(string reason)
    {
        if (closed)
        {
            return;
        }
        requested++;
        refreshReason = reason;
        cancel?.Cancel();
        refresh.Stop();
        bool recognized = XmakeService.IsWorkspace(root);
        bool wasXmake = IsXmake;
        IsXmake = recognized;
        IsEvaluating = recognized && WorkspaceTrust.Trusts(root);
        Problem = recognized && !IsEvaluating ? TrustRequired : null;
        if (IsEvaluating && operation is null)
        {
            refresh.Restart();
        }
        else if (!IsEvaluating)
        {
            operation?.Cancel();
            Snapshot = null;
            configuration?.Dispose();
            configuration = null;
        }
        if (recognized || wasXmake)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void FilesChanged(FolderChanges changes)
    {
        string? input = changes.Paths.FirstOrDefault(path => PathNames.Same(path, projectFile) || IsXmake && IsProjectInput(path));
        string? membership = IsXmake ? changes.ComeOrGone.FirstOrDefault(ChangesMembership) : null;
        if ((input ?? membership) is { } changed)
        {
            Refresh($"{Path.GetRelativePath(root, changed)} changed");
        }
        else if (changes.Lost)
        {
            if (operation is null)
            {
                Refresh();
            }
            else
            {
                // A busy build can overflow the watcher with output changes. Recheck once Xmake releases the workspace.
                recheckAfterOperation = true;
            }
        }
    }

    private bool IsProjectInput(string path) =>
        ChangesMembership(path) && (Path.GetExtension(path).Equals(".lua", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path) == "xmake-requires.lock" || Snapshot?.ProjectFiles.Contains(path, PathNames.Comparer) == true);

    private bool ChangesMembership(string path)
    {
        string build = Snapshot?.Configuration.BuildDirectory ?? Path.Combine(root, "build");
        return !PathNames.Same(path, root) && !PathNames.Within(path, Path.Combine(root, ".xmake"))
            && (PathNames.Same(root, build) || !PathNames.Within(path, build));
    }

    private async Task EvaluateAsync()
    {
        int read = requested;
        Task before = running;
        using CancellationTokenSource current = new();
        cancel = current;
        running = ReadAsync();
        try
        {
            await running;
        }
        finally
        {
            if (cancel == current)
            {
                cancel = null;
            }
        }

        async Task ReadAsync()
        {
            await before;
            if (!Current())
            {
                return;
            }
            try
            {
                worker = BackgroundWork.Run(() => evaluate(root, NewOutputDirectory(), current.Token), current.Token);
                XmakeWorkspace snapshot = await worker;
                if (!Current())
                {
                    return;
                }
                if (WorkspaceTrust.Trusts(root))
                {
                    projectStamp = FileStamp.Of(projectFile);
                    configurationStamp = FileStamp.Of(snapshot.Configuration.ConfigurationFile);
                    Snapshot = snapshot;
                    WatchConfiguration(snapshot.Configuration.ConfigurationFile);
                }
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested)
            {
            }
            catch (Exception problem) when (problem is XmakeException or UntrustedFolderException or IOException or UnauthorizedAccessException)
            {
                if (Current() && WorkspaceTrust.Trusts(root))
                {
                    Snapshot = null;
                    Problem = problem.Message;
                    PassengerLog.Write($"Xmake workspace {root}: {problem.Message}");
                }
            }
            finally
            {
                if (Current())
                {
                    IsEvaluating = false;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        bool Current() => !closed && read == requested && !current.IsCancellationRequested;
    }

    private string NewOutputDirectory()
    {
        // Evaluation is serialized, and the directory is removed after the last evaluation ends.
        scratch ??= Directory.CreateTempSubdirectory("pitstop-xmake-").FullName;
        return Path.Combine(scratch, Guid.NewGuid().ToString("N"));
    }

    private void WatchConfiguration(string path)
    {
        if (configuration is { } watched && PathNames.Same(watched.Path, path))
        {
            return;
        }
        configuration?.Dispose();
        configuration = new WatchedFile(path, "Xmake configuration", _ =>
        {
            UiThread.Post(() =>
            {
                if (configuration is { } active && PathNames.Same(active.Path, path))
                {
                    Refresh("the active Xmake configuration file changed");
                }
            });
            return true;
        }, asSeen: true);
    }

    internal void LookAgain() => configuration?.LookAgain();

    internal async Task CloseAsync()
    {
        closed = true;
        refresh.Stop();
        cancel?.Cancel();
        operation?.Cancel();
        configuration?.Dispose();
        configuration = null;
        if (worker is Task active)
        {
            // The window's dispatcher may already have stopped. The worker is independent of it.
            await active.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        if (scratch is { } directory)
        {
            await BackgroundWork.Run(() =>
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
                {
                    PassengerLog.Write($"Could not remove Xmake's temporary directory at {directory}. {problem.Message}");
                }
            }).ConfigureAwait(false);
        }
    }
}

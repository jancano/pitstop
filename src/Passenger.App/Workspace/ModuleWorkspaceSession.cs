using Passenger.Core;
using Passenger.Editor;
using Pitstop.Modules;
using Pitstop.Xmake;

namespace Passenger.App.Workspace;

public sealed class ModuleWorkspaceSession
{
    private readonly XmakeWorkspaceSession xmake;
    private readonly ModuleScanner scanner = new();
    private readonly Debounce refresh;
    private XmakeWorkspace? desired;
    private CancellationTokenSource? active;
    private Task<ModuleWorkspace>? worker;
    private bool closed;

    internal ModuleWorkspaceSession(XmakeWorkspaceSession xmake)
    {
        this.xmake = xmake;
        refresh = new(TimeSpan.FromMilliseconds(350), ScanAsync);
        xmake.Changed += XmakeChanged;
    }

    public ModuleWorkspace? Snapshot { get; private set; }
    public string Status { get; private set; } = "Waiting for Xmake configuration";
    public event EventHandler? Changed;

    private void XmakeChanged(object? sender, EventArgs args)
    {
        XmakeWorkspace? next = !xmake.IsEvaluating && xmake.Problem is null ? xmake.Snapshot : null;
        if (!ReferenceEquals(desired, next) || next is null)
        {
            desired = next;
            Refresh();
        }
    }

    internal void Refresh()
    {
        if (closed)
        {
            return;
        }
        active?.Cancel();
        refresh.Stop();
        Snapshot = null;
        Status = desired is null ? xmake.Problem ?? "Waiting for Xmake configuration" : "Scanning C++ module dependencies…";
        Changed?.Invoke(this, EventArgs.Empty);
        if (desired is not null)
        {
            refresh.Restart();
        }
    }

    internal void FilesChanged(FolderChanges changes)
    {
        if (desired is not { } workspace)
        {
            return;
        }
        if (changes.Lost || changes.Paths.Any(path => PathNames.Within(path, workspace.Root)
            && !PathNames.Within(path, Path.Combine(workspace.Root, ".xmake"))
            && (!PathNames.Within(path, workspace.Configuration.BuildDirectory) || workspace.Targets.Any(target => target.SourceFiles.Contains(path, PathNames.Comparer)))
            && Path.GetExtension(path).ToLowerInvariant() is ".c" or ".h" or ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh" or ".hxx" or ".cppm" or ".ixx" or ".mpp" or ".cxxm" or ".ccm" or ".inc"))
        {
            Refresh();
        }
    }

    private async Task ScanAsync()
    {
        if (desired is not { } workspace || closed || !WorkspaceTrust.Trusts(workspace.Root))
        {
            return;
        }
        using CancellationTokenSource current = new();
        active = current;
        try
        {
            worker = BackgroundWork.Run(() => scanner.ScanAsync(workspace, current.Token), current.Token);
            ModuleWorkspace snapshot = await worker;
            if (!closed && active == current && !current.IsCancellationRequested && ReferenceEquals(desired, workspace) && WorkspaceTrust.Trusts(workspace.Root))
            {
                Snapshot = snapshot;
                Status = snapshot.Problem ?? (snapshot.IsComplete ? "Module scan complete" : "Module scan incomplete. Some dependencies or sources could not be resolved.");
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
        }
        catch (UntrustedFolderException)
        {
        }
        finally
        {
            if (active == current)
            {
                active = null;
            }
        }
    }

    internal async Task CloseAsync()
    {
        closed = true;
        Snapshot = null;
        xmake.Changed -= XmakeChanged;
        refresh.Stop();
        active?.Cancel();
        if (worker is Task pending)
        {
            await pending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

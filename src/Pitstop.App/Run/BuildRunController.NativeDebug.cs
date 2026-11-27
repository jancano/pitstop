using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Pitstop.App.Debugging;
using Pitstop.App.Settings;
using Pitstop.App.Workspace;
using Pitstop.Core;
using Pitstop.NativeDebug;
using Pitstop.ProjectSystem;
using Pitstop.Xmake;

namespace Pitstop.App.Run;

public sealed partial class BuildRunController
{
    private (RunConfiguration Configuration, string? Arguments)? lastNativeDebug;

    internal static bool NativeDebugProfile => OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64;

    internal string? WhyNotDebugXmake(RunConfiguration? configuration, bool attaching = false) =>
        !NativeDebugProfile ? "The verified native debugger profile requires Windows x64."
        : session.IsEnded ? Refusals.WorkspaceEnding
        : session.Debug.Tabs.Any(tab => tab.IsLive && tab.Session?.Engine == DebugEngine.Native) ? "Stop the active native debug session first."
        : parts.Work.Of(AppActions.Debug) is not null || IsBuilding ? "Wait for the active build or debug launch."
        : session.Xmake is not { } xmake ? "Open an Xmake workspace to debug a native application."
        : xmake.WhyNotOperate is { } problem ? problem
        : !attaching && (configuration is not { IsXmake: true } || xmake.Snapshot?.Targets.Any(
            target => target.Kind == "binary" && target.Name == configuration.XmakeTarget) != true)
            ? "Select an executable Xmake target."
        : null;

    private async Task<string?> DebugXmakeAsync(RunConfiguration chosen, string? arguments, CancellationToken cancellation)
    {
        if (WhyNotDebugXmake(chosen) is { } refusal)
        {
            return Said(refusal);
        }
        using Working work = parts.Work.Begin(AppActions.Debug, $"Preparing {chosen.Name} for native debugging…")!;
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, work.Stopping);
        try
        {
            string? issue = await StartAsync();
            work.Tell(issue is null, issue ?? $"Debugging {chosen.Name} with LLDB");
            return issue;
        }
        catch (OperationCanceledException)
        {
            return Said($"Debugging {chosen.Name} was canceled.");
        }
        catch (Exception error) when (error is NativeDebugException or XmakeException or IOException or ArgumentException)
        {
            return Said(error.Message);
        }

        async Task<string?> StartAsync()
        {
            if (await parts.Editors.SaveAllLandedAsync(work, stop.Token) is { } unsaved)
            {
                return Said(unsaved);
            }
            XmakeWorkspaceSession xmake = session.Xmake!;
            using XmakeWorkspaceSession.Operation? operation = xmake.BeginOperation();
            if (operation is null)
            {
                return Said(xmake.WhyNotOperate ?? "Wait for Xmake to update its configuration.");
            }
            using CancellationTokenSource preparing = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, operation.Stopping);
            XmakeWorkspace workspace = operation.Snapshot;
            if (chosen.BuildBeforeLaunch && await BuildXmakeAsync(BuildKind.Build, [chosen.XmakeTarget], BuildCause.BeforeLaunch, preparing.Token, operation)
                is { Succeeded: false } failed)
            {
                return Said(failed.Message);
            }
            if (!operation.IsCurrent)
            {
                return Said($"Debug launch canceled because {operation.ObsoleteReason}.");
            }
            string argumentsText = arguments ?? chosen.Arguments;
            NativeLaunchOptions options = new(string.IsNullOrEmpty(argumentsText) ? default : [.. CommandLine.Split(argumentsText)],
                chosen.WorkingDirectory, chosen.Environment.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
                chosen.Console == ConsoleKind.Terminal ? NativeConsole.IntegratedTerminal : NativeConsole.Internal,
                BuildBeforeDebug: false, StopOnEntry: chosen.NativeStopOnEntry, SourceMaps: NativeSourceMaps(chosen.NativeSourceMapText, workspace.Root));
            NativeLaunchPlan plan = await BackgroundWork.Run(() => XmakeDebugLaunch.ResolveAsync(workspace, chosen.XmakeTarget, options, preparing.Token), preparing.Token);
            LldbInstallation installation = await FindLldbAsync(workspace.Root, preparing.Token);
            if (!operation.IsCurrent)
            {
                return Said($"Debug launch canceled because {operation.ObsoleteReason}.");
            }
            preparing.Token.ThrowIfCancellationRequested();
            parts.Dock.Show(ToolWindowIds.Debug);
            string warning = workspace.Configuration.Mode.Equals("debug", StringComparison.OrdinalIgnoreCase)
                ? $"LLDB {installation.LldbVersion} · {workspace.Configuration.Mode} · {plan.Target}"
                : $"Debugging the active {workspace.Configuration.Mode} artifact. Optimized code or missing symbols can limit stepping and inspection.";
            string? problem = await session.Debug.OpenNativeAsync(chosen.Name, installation, workspace, session.Modules?.Snapshot,
                SafeDebuggerDirectory(), plan, null, preparing.Token, warning);
            if (problem is null && operation.IsCurrent)
            {
                lastNativeDebug = (chosen, arguments);
            }
            else if (problem is null)
            {
                await session.Debug.StopAsync(chosen.Name);
                return Said($"Debug launch canceled because {operation.ObsoleteReason}.");
            }
            return problem;
        }
    }

    internal async Task<string?> RestartNativeDebugAsync()
    {
        if (lastNativeDebug is not { } last || session.Debug.Session is { Engine: DebugEngine.Native, Attached: true, State: not DebugState.Ended })
        {
            return Said("Select a launched native debug session to restart.");
        }
        if (session.Debug.Session is { Engine: DebugEngine.Native, State: not DebugState.Ended } active)
        {
            await active.StopAsync();
        }
        return await DebugXmakeAsync(last.Configuration, last.Arguments, CancellationToken.None);
    }

    internal async Task<string?> AttachNativeAsync(int processId, string name)
    {
        if (WhyNotDebugXmake(null, attaching: true) is { } refusal)
        {
            return Said(refusal);
        }
        using Working work = parts.Work.Begin(AppActions.Debug, $"Attaching LLDB to {name}…")!;
        XmakeWorkspace workspace = session.Xmake!.Snapshot!;
        try
        {
            LldbInstallation installation = await FindLldbAsync(workspace.Root, work.Stopping);
            if (session.IsEnded || session.Xmake.Snapshot != workspace || !DotnetProcesses.Runs(processId))
            {
                return Said("The process or workspace changed. Refresh Attach to Process and try again.");
            }
            parts.Dock.Show(ToolWindowIds.Debug);
            string? problem = await session.Debug.OpenNativeAsync(name, installation, workspace, session.Modules?.Snapshot,
                SafeDebuggerDirectory(), null, processId, work.Stopping);
            work.Tell(problem is null, problem ?? $"Attached LLDB to {name}");
            return problem;
        }
        catch (OperationCanceledException)
        {
            return Said("Attach to Process was canceled.");
        }
        catch (NativeDebugException error)
        {
            return Said(error.Message);
        }
    }

    private Task<LldbInstallation> FindLldbAsync(string workspace, CancellationToken cancel)
    {
        WorkspaceTrust.Check(workspace);
        NativeToolPaths paths = parts.Settings().NativeTools;
        return BackgroundWork.Run(() => LldbDiscovery.DiscoverAsync(paths.LldbDap, paths.LlvmBin, cancel,
            path => !PathNames.Within(path, workspace) || WorkspaceTrust.Trusts(workspace)), cancel);
    }

    internal Task<LldbInstallation> FindNativeDebuggerAsync(string workspace, CancellationToken cancel) => FindLldbAsync(workspace, cancel);

    internal Task<string?> StartNativeConfigurationAsync(string? configuration, CancellationToken cancel)
    {
        RunConfiguration? chosen = configuration is null ? session.Configurations.Chosen
            : session.Configurations.All.FirstOrDefault(item => item.IsXmake && item.Name == configuration);
        return chosen is { IsXmake: true } ? DebugXmakeAsync(chosen, null, cancel)
            : Task.FromResult<string?>("Select an evaluated executable Xmake configuration.");
    }

    private static string SafeDebuggerDirectory()
    {
        string directory = Path.Combine(State.StateStore.DefaultRoot, "cache", "lldb");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static ImmutableArray<NativeSourceMap> NativeSourceMaps(string text, string workspace)
    {
        ImmutableArray<NativeSourceMap>.Builder maps = ImmutableArray.CreateBuilder<NativeSourceMap>();
        foreach (string line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Split("=>", 2, StringSplitOptions.TrimEntries) is not [string from, string to] || !Path.IsPathFullyQualified(from) || to.Length == 0)
            {
                throw new NativeDebugException(NativeDebugFailure.InvalidLaunch, "Use one 'absolute build directory => local source directory' mapping per line.");
            }
            string local = Path.GetFullPath(to, workspace);
            if (!Directory.Exists(local) || maps.Any(map => PathNames.Same(map.BuildPath, from)))
            {
                throw new NativeDebugException(NativeDebugFailure.InvalidLaunch, "Each debug source mapping needs a distinct build directory and an existing local directory.");
            }
            maps.Add(new(from, PathNames.Canonical(local)));
        }
        return maps.ToImmutable();
    }
}

using System.Diagnostics;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Text;
using Pitstop.Xmake;

namespace Passenger.App.Run;

public sealed partial class BuildRunController
{
    internal const string XmakeDebugUnavailable = "Native debugging is unavailable. Use Run for Xmake targets.";
    internal const string XmakeProfileUnavailable = "Native profiling is unavailable for Xmake targets.";

    private (BuildKind Kind, IReadOnlyList<string> Targets)? lastXmakeBuild;
    private XmakeBuildOutput? xmakeOutput;
    private XmakeWorkspace? xmakeProblems;
    private Working? xmakeBuildWork;
    private Task xmakeRunEnded = Task.CompletedTask;

    private bool PrefersXmake => parts.PreferXmake?.Invoke() == true;

    internal string? WhyNotBuildXmake() => IsBuilding ? Refusals.Building
        : session.Xmake is { } native ? native.WhyNotOperate : "Open an Xmake workspace to build its targets.";

    private void KeepBuildLine(string line)
    {
        builtLines.Enqueue(line);
        if (builtLines.Count > BuiltLinesKept)
        {
            builtLines.Dequeue();
        }
    }

    private void XmakeChanged(XmakeWorkspaceSession native)
    {
        if (!session.IsEnded && xmakeProblems is { } previous && (native.IsEvaluating || native.Snapshot != previous))
        {
            xmakeProblems = null;
            session.Problems.Clear();
            ShowProblems();
        }
    }

    internal void StopBuild()
    {
        xmakeBuildWork?.Cancel();
        session.Builder.Stop();
    }

    internal void BuildXmake(BuildKind kind, IReadOnlyList<string> targets, Action<BuildOutcome>? then = null, BuildCause cause = BuildCause.Explicit)
    {
        IdeErrors.Observe(BuildAsync());

        async Task BuildAsync()
        {
            BuildOutcome outcome = await BuildXmakeAsync(kind, targets, cause, CancellationToken.None);
            then?.Invoke(outcome);
        }
    }

    private async Task<BuildOutcome> BuildXmakeAsync(BuildKind kind, IReadOnlyList<string> targets, BuildCause cause, CancellationToken cancel,
        XmakeWorkspaceSession.Operation? launch = null)
    {
        UiThread.Check();
        if (session.IsEnded || IsBuilding || (launch is null && WhyNotBuildXmake() is not null))
        {
            string refusal = session.IsEnded ? Refusals.SolutionClosed : WhyNotBuildXmake() ?? Refusals.Building;
            Said(refusal);
            return new(BuildStatus.Refused, refusal);
        }
        string verb = kind switch { BuildKind.Clean => "Clean", BuildKind.Rebuild => "Rebuild", _ => "Build" };
        string subject = targets.Count == 0 ? "Xmake workspace" : string.Join(", ", targets);
        if (parts.Work.Begin(AppActions.Build, $"{verb} {subject}…") is not { } work)
        {
            return new(BuildStatus.Refused, Said(Refusals.Building));
        }

        using (work)
        using (CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(cancel, work.Stopping))
        {
            TaskCompletionSource done = building = new(TaskCreationOptions.RunContinuationsAsynchronously);
            xmakeBuildWork = work;
            BackgroundWork.Track(done.Task);
            parts.Views.Build.Clear();
            parts.Views.Build.SetRunning(true);
            if (OpensBuildWindow(cause))
            {
                parts.Dock.Show(ToolWindowIds.Build);
            }
            XmakeWorkspaceSession.Operation? operation = launch;
            BuildOutcome outcome;
            try
            {
                outcome = await ExecuteAsync();
            }
            catch (OperationCanceledException)
            {
                outcome = new(BuildStatus.Stopped, $"{verb} stopped");
            }
            catch (Exception problem) when (problem is XmakeException or UntrustedFolderException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                outcome = new(BuildStatus.NotStarted, problem.Message);
            }
            finally
            {
                xmakeOutput = null;
                xmakeBuildWork = null;
                building = null;
                if (launch is null)
                {
                    operation?.Dispose();
                }
                done.TrySetResult();
            }
            if (!session.IsEnded)
            {
                parts.Views.Build.Append($"\n{outcome.Message}\n");
                parts.Views.Build.SetRunning(false);
                work.Tell(outcome.Succeeded, outcome.Message);
                parts.Say(outcome.Message, Place(outcome));
                if (ShowsBuildResults(outcome))
                {
                    parts.Dock.Show(ToolWindowIds.Build);
                }
                BuildFinished?.Invoke(this, outcome);
            }
            return outcome;

            async Task<BuildOutcome> ExecuteAsync()
            {
                if (launch is null && await parts.Editors.SaveAllLandedAsync(work, stopping.Token) is { } unsaved)
                {
                    stopping.Token.ThrowIfCancellationRequested();
                    return new(BuildStatus.NotStarted, unsaved);
                }
                stopping.Token.ThrowIfCancellationRequested();
                XmakeWorkspaceSession native = session.Xmake!;
                operation ??= native.BeginOperation();
                if (operation is not { } active)
                {
                    return new(BuildStatus.Refused, native.WhyNotOperate ?? Refusals.Building);
                }
                XmakeWorkspace snapshot = active.Snapshot;
                using CancellationTokenSource commandStop = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, active.Stopping);
                commandStop.Token.ThrowIfCancellationRequested();
                XmakeBuildKind operationKind = kind switch
                {
                    BuildKind.Build => XmakeBuildKind.Build,
                    BuildKind.Rebuild => XmakeBuildKind.Rebuild,
                    BuildKind.Clean => XmakeBuildKind.Clean,
                    _ => throw new XmakeException("Xmake supports Build, Rebuild and Clean here."),
                };
                ProcessStartInfo start = XmakeOperations.Build(snapshot, operationKind, targets);
                lastXmakeBuild = (kind, [.. targets]);
                builtLines.Clear();
                XmakeBuildOutput output = xmakeOutput = new(snapshot.Root);
                XmakeConfiguration config = snapshot.Configuration;
                parts.Views.Build.Append($"{verb} {subject} ({config.Mode}, {config.Platform} {config.Architecture})\n");
                Stopwatch duration = Stopwatch.StartNew();
                using CancellationTokenRegistration stopped = commandStop.Token.Register(() => UiThread.Post(() =>
                {
                    if (xmakeOutput == output)
                    {
                        session.Builder.Stop();
                    }
                }));
                if (await session.Builder.StartAsync(start) is { } unstarted)
                {
                    commandStop.Token.ThrowIfCancellationRequested();
                    return new(BuildStatus.NotStarted, unstarted);
                }
                int code = await session.Builder.Ended;
                string time = $"({Numbers.Fixed(duration.Elapsed.TotalSeconds, 2)} s)";
                if (commandStop.IsCancellationRequested || session.Builder.WasStopped)
                {
                    if (active.IsCurrent && xmakeProblems == snapshot)
                    {
                        parts.Views.Build.Append("Problems still shows the previous completed build for this configuration.\n");
                    }
                    return new(BuildStatus.Stopped, $"{verb} stopped {time}") { Output = [.. builtLines] };
                }
                if (active.ObsoleteReason is { } obsolete)
                {
                    return new(BuildStatus.Stopped, $"{verb} results discarded because {obsolete} {time}") { Output = [.. builtLines] };
                }
                List<Problem> problems = NativeProblems(output.Complete(), $"{subject} ({config.Mode})");
                if (code != 0 && !problems.Any(problem => problem.Severity == ProblemSeverity.Error))
                {
                    problems.Add(new(ProblemSeverity.Error, $"Xmake ended with {ExitCodes.Said(code)}. See the Build output for details.", null, null, default, subject));
                }
                session.Problems.Clear();
                session.Problems.AddRange(problems);
                xmakeProblems = snapshot;
                ShowProblems();
                return new(code == 0 ? BuildStatus.Succeeded : BuildStatus.Failed,
                    code == 0 ? $"{verb} succeeded {time}" : $"{verb} failed: Xmake ended with {ExitCodes.Said(code)} {time}")
                {
                    Problems = [.. problems],
                    Output = [.. builtLines],
                };
            }
        }
    }

    private static List<Problem> NativeProblems(IReadOnlyList<XmakeDiagnostic> diagnostics, string project)
    {
        List<Problem> found = [.. diagnostics.Select(diagnostic => new Problem(diagnostic.Severity switch
        {
            XmakeDiagnosticSeverity.Warning => ProblemSeverity.Warning,
            XmakeDiagnosticSeverity.Note => ProblemSeverity.Message,
            _ => ProblemSeverity.Error,
        }, diagnostic.Message, null, diagnostic.Path,
            diagnostic.Path is null ? default : new TextPosition(diagnostic.Line - 1, diagnostic.Column - 1), project))];
        int primary = -1;
        for (int index = 0; index < found.Count; index++)
        {
            Problem problem = found[index];
            if (problem.Severity != ProblemSeverity.Message)
            {
                primary = problem.Path is null ? -1 : index;
            }
            else if (primary >= 0 && problem.Path is not null)
            {
                Problem owner = found[primary];
                found[primary] = owner with { Related = [.. owner.Related, problem] };
            }
        }
        return found;
    }

    private async Task<string?> RunXmakeAsync(RunConfiguration chosen, string? arguments, Working work)
    {
        using IDisposable held = BackgroundWork.Scope();
        await xmakeRunEnded;
        if (await parts.Editors.SaveAllLandedAsync(work, work.Stopping) is { } unsaved)
        {
            return Said(unsaved);
        }
        if (work.IsStopped || session.IsEnded)
        {
            return Said(Canceled(work, chosen.Name));
        }
        if (session.Xmake?.BeginOperation() is not { } operation)
        {
            return Said(session.Xmake?.WhyNotOperate ?? "Open the workspace for this Xmake target.");
        }
        bool handedOver = false;
        try
        {
            XmakeWorkspace snapshot = operation.Snapshot;
            ProcessStartInfo start = XmakeOperations.Run(snapshot, chosen.XmakeTarget, CommandLine.Split(arguments ?? chosen.Arguments), chosen.WorkingDirectory);
            if (chosen.BuildBeforeLaunch && await BuildXmakeAsync(BuildKind.Build, [chosen.XmakeTarget], BuildCause.BeforeLaunch, work.Stopping, operation) is { Succeeded: false } failed)
            {
                return Said(failed.Message);
            }
            if (!operation.IsCurrent || work.IsStopped || session.IsEnded)
            {
                return Said(work.IsStopped || session.IsEnded ? Canceled(work, chosen.Name) : "The Xmake configuration changed. Run again after evaluation finishes.");
            }

            ran = chosen.Name;
            RunProject = snapshot.Root;
            session.RunPlan = null;
            parts.Views.Run.Fold = null;
            UnfollowWatched();
            string running = $"Running {chosen.XmakeTarget} ({snapshot.Configuration.Mode}) with Xmake";
            string? problem;
            if (chosen.Console == ConsoleKind.Terminal)
            {
                problem = await RunInTerminalAsync(chosen.Name, start, chosen.Environment, running, work);
            }
            else
            {
                parts.Views.Run.Clear();
                ShowInRun(null);
                session.InTerminal = null;
                parts.Views.Run.SetRunning(true);
                parts.Dock.Show(ToolWindowIds.Run);
                Running(work, running);
                problem = Started(await session.Runner.StartAsync(start, chosen.Environment));
            }
            if (problem is not null)
            {
                return problem;
            }
            IRunningProgram program = session.RunProgram;
            if (session.IsEnded || work.IsStopped || !operation.IsCurrent)
            {
                program.Stop();
            }
            handedOver = true;
            xmakeRunEnded = EndXmakeRunAsync(operation, program);
            return null;
        }
        catch (Exception problem) when (problem is XmakeException or UntrustedFolderException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Said(problem.Message);
        }
        finally
        {
            if (!handedOver)
            {
                operation.Dispose();
            }
        }
    }

    private static async Task EndXmakeRunAsync(XmakeWorkspaceSession.Operation operation, IRunningProgram program)
    {
        Task<int> ended = program.Ended;
        using (operation)
        using (operation.Stopping.Register(() => UiThread.Post(() =>
        {
            if (!ended.IsCompleted)
            {
                program.Stop();
            }
        })))
        {
            await ended;
        }
    }
}

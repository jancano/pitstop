using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.NativeDebug;
using Pitstop.ProjectSystem;

namespace Pitstop.NativeDebugProbe;

internal sealed partial class DebugProbe
{
    private async Task FunctionBreakpointAsync(NativeLaunchPlan plan)
    {
        await using NativeDebugSession session = await StartRuntimeAsync("function-breakpoint", plan);
        await session.LaunchAsync(plan with { BuildBeforeDebug = false }, async (adapter, token) =>
        {
            SetBreakpointsResponse found = await adapter.RequestAsync<SetBreakpointsResponse>(DapCommands.SetFunctionBreakpoints,
                new SetFunctionBreakpointsArguments([new("inspect")]), token);
            Require(found.Breakpoints.Single().Verified, "The inspect function breakpoint was not verified.");
        }, cancel: cancel);
        (StoppedEvent stopped, DapStackFrame frame) = await StopAsync(session, "main.cpp");
        Require(frame.Name.Contains("inspect", StringComparison.Ordinal), "The function breakpoint stopped in a different function.");
        await session.RequestAsync<ContinueResponse>(DapCommands.Continue, new ThreadArguments(stopped.ThreadId!.Value), cancel);
        await UntilExitAsync(session, 31);
    }

    private async Task ConditionalBreakpointsAsync(NativeLaunchPlan plan)
    {
        string root = plan.Workspace.Root;
        const string logged = "fixture logpoint";
        await using NativeDebugSession session = await StartRuntimeAsync("conditional-hit-log-breakpoints", plan);
        await session.LaunchAsync(plan with { Arguments = ["breakpoints"], BuildBeforeDebug = false }, async (adapter, token) =>
        {
            await SetAsync("main.cpp", "locals-stop", condition: "seed == 11");
            await SetAsync("c_math.c", "c-stop", log: logged);
            await SetAsync("library.cpp", "library-stop", hit: "2");

            async Task SetAsync(string file, string marker, string? condition = null, string? log = null, string? hit = null)
            {
                string path = Path.Combine(root, file);
                SetBreakpointsResponse found = await adapter.RequestAsync<SetBreakpointsResponse>(DapCommands.SetBreakpoints,
                    new SetBreakpointsArguments(new DapSource { Path = path },
                        [new DapSourceBreakpoint(Line(path, marker), Condition: condition, HitCondition: hit, LogMessage: log)]), token);
                Require(found.Breakpoints.Single().Verified, $"The fixture breakpoint in {file} did not verify.");
            }
        }, cancel: cancel);
        (StoppedEvent stopped, DapStackFrame frame) = await StopAsync(session, "main.cpp");
        Require((await VariablesAsync(session, frame.Id)).Any(value => value is { Name: "seed", Value: "11" }),
            "A stop occurred before the matching source condition and hit count.");
        await session.RequestAsync<ContinueResponse>(DapCommands.Continue, new ThreadArguments(stopped.ThreadId!.Value), cancel);
        bool logSeen = false;
        (stopped, _) = await StopAsync(session, "library.cpp", output => logSeen |= output.Output.Contains(logged, StringComparison.Ordinal));
        Require(logSeen, "The C source logpoint did not emit output while continuing to the second DLL hit.");
        await session.RequestAsync<ContinueResponse>(DapCommands.Continue, new ThreadArguments(stopped.ThreadId!.Value), cancel);
        await UntilExitAsync(session, 31);
    }

    private async Task ExceptionProbeAsync(NativeLaunchPlan plan)
    {
        await using NativeDebugSession session = await StartRuntimeAsync("cpp-exception", plan);
        Require(JsonMembers.Items(session.AdvertisedCapabilities, "exceptionBreakpointFilters")
            .Any(filter => JsonMembers.Text(filter, "filter") == "cpp_throw"), "No C++ throw filter was advertised.");
        await session.LaunchAsync(plan with { Arguments = ["exception"], BuildBeforeDebug = false }, async (adapter, token) =>
        {
            await adapter.RequestAsync<SetExceptionBreakpointsResponse>(DapCommands.SetExceptionBreakpoints,
                new SetExceptionBreakpointsArguments(["cpp_throw"]), token);
        }, cancel: cancel);
        (StoppedEvent stopped, _) = await StopAsync(session);
        Require(stopped.Reason == StopReasons.Exception, "The C++ throw filter did not report an exception stop.");
        ExceptionInfoResponse information = await session.RequestAsync<ExceptionInfoResponse>(DapCommands.ExceptionInfo,
            new ThreadArguments(stopped.ThreadId!.Value), cancel);
        Require(information.ExceptionId.Length > 0, "LLDB did not identify the C++ exception.");
        await session.RequestAsync<ContinueResponse>(DapCommands.Continue, new ThreadArguments(stopped.ThreadId!.Value), cancel);
        await UntilExitAsync(session, 7);
    }
}

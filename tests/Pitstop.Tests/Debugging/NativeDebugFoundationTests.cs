using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.NativeDebug;
using Pitstop.ProjectSystem;
using Pitstop.Tests.Debugging.Helpers;

namespace Pitstop.Tests.Debugging;

public sealed class NativeDebugFoundationTests
{
    [Theory]
    [InlineData("early", false)]
    [InlineData("deferred", true)]
    public async Task Launch_configures_the_adapter_regardless_of_response_order(string mode, bool deferred)
    {
        string root = Fixture(mode);
        CancellationToken cancel = TestContext.Current.CancellationToken;
        ConcurrentQueue<NativeDebugPacket> trace = new();
        LldbInstallation adapter = await LldbDiscovery.DiscoverAsync(Adapter, cancel: cancel);
        await using NativeDebugSession session = await NativeDebugSession.StartAsync(adapter, root, root, cancel, trace.Enqueue);
        int builds = 0;
        await session.LaunchAsync(Plan(root), (_, _) => Task.CompletedTask, token =>
        {
            token.ThrowIfCancellationRequested();
            ++builds;
            return Task.CompletedTask;
        }, cancel);
        Assert.Equal(1, builds);
        Assert.Equal(DapEvents.Initialized, (await session.ReadEventAsync(cancel)).Name);
        Assert.Equal(DapEvents.Stopped, (await session.ReadEventAsync(cancel)).Name);
        NativeDebugPacket[] packets = trace.ToArray();
        int launchReply = Array.FindIndex(packets, packet => packet.Kind == "response" && packet.Name == DapCommands.Launch);
        int initialized = Array.FindIndex(packets, packet => packet.Kind == "event" && packet.Name == DapEvents.Initialized);
        Assert.True(initialized >= 0 && launchReply >= 0);
        Assert.Equal(deferred, initialized < launchReply);
        await session.DisposeAsync();
        Assert.Equal("terminate", File.ReadAllText(Path.Combine(root, "disconnect")));
    }

    [Fact]
    public async Task Attach_disconnects_without_claiming_the_process()
    {
        string root = Fixture("early");
        CancellationToken cancel = TestContext.Current.CancellationToken;
        await using NativeDebugSession session = await NativeDebugSession.StartAsync(FakeInstallation, root, root, cancel);
        await session.AttachAsync(Environment.ProcessId, cancel: cancel);
        await session.DisposeAsync();
        Assert.Equal("detach", File.ReadAllText(Path.Combine(root, "disconnect")));
    }

    [Fact]
    public async Task Cancellation_retires_the_session_and_ignores_buffered_events()
    {
        string root = Fixture("early");
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using NativeDebugSession session = await NativeDebugSession.StartAsync(FakeInstallation, root, root, lifetime.Token);
        await session.AttachAsync(Environment.ProcessId, cancel: lifetime.Token);
        using Process adapter = Process.GetProcessById(session.AdapterId);
        await lifetime.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.ReadEventAsync(TestContext.Current.CancellationToken));
        await adapter.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal("detach", File.ReadAllText(Path.Combine(root, "disconnect")));
    }

    [Fact]
    public async Task A_launch_that_omits_initialized_times_out_and_shuts_down()
    {
        string root = Fixture("timeout");
        CancellationToken cancel = TestContext.Current.CancellationToken;
        await using NativeDebugSession session = await NativeDebugSession.StartAsync(FakeInstallation, root, root, cancel,
            requestLimit: TimeSpan.FromSeconds(2));
        using Process adapter = Process.GetProcessById(session.AdapterId);
        NativeDebugException error = await Assert.ThrowsAsync<NativeDebugException>(() => session.LaunchAsync(Plan(root),
            build: _ => Task.CompletedTask, cancel: cancel));
        Assert.Equal(NativeDebugFailure.Timeout, error.Failure);
        Assert.False(DotnetProcesses.Runs(adapter));
    }

    [Fact]
    public async Task An_adapter_crash_closes_the_event_stream()
    {
        string root = Fixture("crash");
        CancellationToken cancel = TestContext.Current.CancellationToken;
        await using NativeDebugSession session = await NativeDebugSession.StartAsync(FakeInstallation, root, root, cancel);
        NativeDebugException error = await Assert.ThrowsAsync<NativeDebugException>(() => session.ReadEventAsync(cancel));
        Assert.Equal(NativeDebugFailure.Adapter, error.Failure);
    }

    [Fact]
    public async Task Untrusted_workspaces_cannot_start_a_debug_session()
    {
        string root = PathNames.Canonical(TestFolders.Create("native-debug-untrusted-"));
        WorkspaceTrust.TakeBack(root);
        using IDisposable asked = WorkspaceTrust.Ask(root);
        await Assert.ThrowsAsync<UntrustedFolderException>(() => NativeDebugSession.StartAsync(FakeInstallation, root, root,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Windows_reference_profile_runs_the_real_DAP_probe()
    {
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        string? xmake = Environment.GetEnvironmentVariable("PITSTOP_XMAKE");
        string? configured = Environment.GetEnvironmentVariable(LldbDiscovery.AdapterVariable);
        Assert.SkipWhen(!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64,
            "The reference native debugger probe requires Windows x64.");
        Assert.SkipWhen(llvm is null || !Directory.Exists(Path.Combine(llvm, "bin")) || !File.Exists(xmake),
            "Set PITSTOP_TEST_LLVM_MINGW and PITSTOP_XMAKE to the validated LLVM-MinGW and Xmake installations.");
        Assert.SkipWhen(!File.Exists(configured ?? Path.Combine(llvm!, "bin", "lldb-dap.exe")),
            "The installed LLVM distribution must include lldb-dap. Set PITSTOP_LLDB_DAP to its absolute path.");
        CancellationToken cancel = TestContext.Current.CancellationToken;
        string reports = TestFolders.Create("native-debug-results-");
        ProcessStartInfo command = ChildProcesses.Command(Debuggees.Apphost("Pitstop.NativeDebugProbe"),
            ["--trust-fixtures", llvm!, reports], Debuggees.Root);
        ChildResult result = await ChildProcesses.RunAsync(command, limit: TimeSpan.FromMinutes(5), cancel: cancel);
        Assert.True(result.Succeeded, result.Output + result.Error);
        Assert.True(File.Exists(Path.Combine(reports, "capabilities.json")));
    }

    private static string Adapter => Debuggees.Apphost("NativeDapTestAdapter");

    private static LldbInstallation FakeInstallation => new(Adapter, new(23, 1, 6), new(23, 1, 6), null, null, null);

    private static string Fixture(string mode)
    {
        string root = PathNames.Canonical(TestFolders.Create("native-debug-"));
        TestFolders.Write(root, "mode", mode);
        WorkspaceTrust.Trust([root]);
        return root;
    }

    private static NativeLaunchPlan Plan(string root)
    {
        string executable = Path.Combine(root, Path.GetFileName(Adapter));
        File.Copy(Adapter, executable);
        return new(new FakeWorkspace(root), "fake", executable, root, ["a b", "a\"b", "", "café"],
            ImmutableDictionary<string, string>.Empty.Add("PITSTOP_FIXTURE", "test"), [],
            NativeConsole.Internal, true, false, [new("build-path", root)]);
    }

    private sealed record FakeWorkspace(string Root) : INativeWorkspace
    {
        public string ProviderId => "fake";
        public string ProviderName => "Fake";
        public NativeConfiguration Configuration => new("debug", "local", "x64", null);
        public IReadOnlyList<INativeTarget> Targets => [];
        public NativeCompilationDatabases? CompilationDatabases => null;
        public string? CompilationDatabaseProblem => null;
        public ImmutableArray<string> DependenciesOf(string target) => [];
        public string? MatchOutputTarget(string? output, IReadOnlyCollection<string> candidates) => null;
    }
}

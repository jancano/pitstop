using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Pitstop.App;
using Pitstop.App.Mcp;
using Pitstop.Core;
using Pitstop.Tests.Debugging.Helpers;
using Pitstop.Modules;
using Pitstop.Xmake;

namespace Pitstop.Tests;

public sealed class McpModuleWorkspaceTests : McpRig
{
    [AvaloniaFact]
    public void An_attached_client_reads_the_current_module_snapshot_and_its_limits()
    {
        string? xmake = Environment.GetEnvironmentVariable(XmakeService.ExecutableVariable);
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        Assert.SkipWhen(!OperatingSystem.IsWindows() || xmake is null || !File.Exists(xmake) || llvm is null
            || !File.Exists(Path.Combine(llvm, "bin", "clang-scan-deps.exe")),
            "Requires Windows x64, PITSTOP_XMAKE set to Xmake 3.1.2 and PITSTOP_TEST_LLVM_MINGW set to LLVM-MinGW 23.1.6.");
        string root = PathNames.Canonical(TestFolders.Create("mcp-modules-"));
        DeleteAtEnd(root);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Debuggees.Root, "examples", "hello-modules")))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }
        using IDisposable asking = WorkspaceTrust.Ask(root);
        WorkspaceTrust.Trust(root);
        Configure("debug");
        MainWindow window = AgentsWindow(root, languageServer: false);
        McpClient client = Await(Waiting.Apart(() => Start(["--mcp-attach", ProcessId])), "attach the MCP client for modules");
        try
        {
            WaitFor(() => window.Session.Modules?.Snapshot?.Named("greetings").Any() == true, "the Debug module snapshot");
            ModuleWorkspace displayed = window.Session.Modules!.Snapshot!;
            window.ShowModules();
            Assert.Contains(window.ExplorerView.Tree.Rows, row => row.Name == "greetings");
            JsonElement all = Query();
            Assert.Equal(displayed.Providers.Length, all.GetProperty("modules").GetArrayLength());
            Assert.Equal("debug", all.GetProperty("configuration").GetString());
            JsonElement limited = Query(new() { ["target"] = "greetings", ["limit"] = 1 });
            Assert.True(limited.GetProperty("truncated").GetBoolean());
            Assert.Equal("greetings", Assert.Single(limited.GetProperty("modules").EnumerateArray()).GetProperty("target").GetString());
            JsonElement described = Assert.Single(Query(new() { ["name"] = "greetings", ["target"] = "greetings" })
                .GetProperty("modules").EnumerateArray());
            Assert.Contains(described.GetProperty("dependencies").EnumerateArray(), item =>
                item.EnumerateObject().Any(property => property.Name.Equals("name", StringComparison.OrdinalIgnoreCase)
                    && property.Value.GetString() == "greetings:punctuation"));
            Assert.Contains(described.GetProperty("consumers").EnumerateArray(), item => item.GetString() == "main.cpp");
            Assert.Contains(described.GetProperty("potentialImpact").EnumerateArray(), item => item.GetString() == "main.cpp");

            Configure("release");
            WaitFor(() => window.Session.Modules?.Snapshot?.Workspace.Configuration.Mode == "release", "the Release module snapshot");
            Assert.Equal("release", Query().GetProperty("configuration").GetString());
            WorkspaceTrust.TakeBack(root);
            WaitFor(() => window.Session.Modules?.Snapshot is null, "trust revocation to clear the module snapshot");
            Assert.False(Query().TryGetProperty("modules", out _));
            window.CloseSolution();
            WaitFor(() => window.Session.Folder is null, "the workspace to close");
            CallToolResult afterClose = UnaskedResult(window, client, McpTools.ModuleWorkspace);
            Assert.DoesNotContain("greetings.cppm", Text(afterClose), StringComparison.Ordinal);
        }
        finally
        {
            WorkspaceTrust.TakeBack(root);
            Gone(client);
            window.Close();
        }

        JsonElement Query(Dictionary<string, object?>? arguments = null) => Unasked(window, client, McpTools.ModuleWorkspace, arguments);

        void Configure(string mode)
        {
            ProcessStartInfo command = ChildProcesses.Command(xmake!, ["f", "-m", mode, "-p", "mingw", "--toolchain=llvm", $"--mingw={llvm}", $"--sdk={llvm}", "--require=no", "-y"], root);
            command.Environment["XMAKE_STATS"] = "false";
            ChildResult result = Ui.Await(ChildProcesses.RunAsync(command, limit: AgentLimit), $"configure Xmake in {mode}", AgentLimit);
            Assert.True(result.Succeeded, result.Output + result.Error);
        }
    }

    [AvaloniaFact]
    public void The_client_distinguishes_an_unresolved_import_from_two_visible_providers()
    {
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        Assert.SkipWhen(!OperatingSystem.IsWindows() || llvm is null || !File.Exists(Path.Combine(llvm, "bin", "clang-scan-deps.exe")),
            "Requires Windows x64 and PITSTOP_TEST_LLVM_MINGW set to LLVM-MinGW 23.1.6.");
        string root = PathNames.Canonical(TestFolders.Create("mcp-module-edges-"));
        DeleteAtEnd(root);
        string script = TestFolders.Write(root, "xmake.lua", "-- The test supplies evaluated target metadata and compilation commands.\n");
        string provider = TestFolders.Write(root, "provider.cppm", "export module shared;\nexport constexpr int value() { return 42; }\n");
        string consumer = TestFolders.Write(root, "consumer.cpp", "import shared;\nimport absent;\nint main() { return value(); }\n");
        string compiler = Path.Combine(llvm!, "bin", "clang++.exe");
        using IDisposable asking = WorkspaceTrust.Ask(root);
        WorkspaceTrust.Trust(root);

        MainWindow window = new() { StartsLanguageServer = false, Autosaves = false, EvaluateXmake = Evaluate };
        window.Show();
        Ui.Await(window.OpenFolder(root), "open the workspace with incomplete compiler bindings");
        window.ApplySettings(window.Settings with { McpServer = true });
        McpClient client = Await(Waiting.Apart(() => Start(["--mcp-attach", ProcessId])), "attach the MCP client for unresolved modules");
        try
        {
            WaitFor(() => window.Session.Modules?.Snapshot is not null, "scan the imports with incomplete bindings");
            Assert.Null(window.Session.Modules!.Snapshot!.Problem);
            JsonElement result = Unasked(window, client, McpTools.ModuleWorkspace);
            Assert.False(result.GetProperty("complete").GetBoolean());
            Assert.Equal(2, result.GetProperty("modules").GetArrayLength());
            Dictionary<string, string?> resolutions = result.GetProperty("unresolved").EnumerateArray()
                .ToDictionary(item => item.GetProperty("name").GetString()!, item => item.GetProperty("resolution").GetString(), StringComparer.Ordinal);
            Assert.Equal("Ambiguous", resolutions["shared"]);
            Assert.Equal("Unresolved", resolutions["absent"]);
            Assert.True(window.Session.Modules.Snapshot.Dependencies.All(edge => edge.Provider is null));
        }
        finally
        {
            WorkspaceTrust.TakeBack(root);
            Gone(client);
            window.Close();
        }

        Task<XmakeWorkspace> Evaluate(string folder, string output, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            Directory.CreateDirectory(output);
            string database = Path.Combine(output, "compile_commands.json");
            File.WriteAllText(database, JsonSerializer.Serialize(new[]
            {
                Command("provider.cppm", "build/.gens/one/shared.pcm", "-x", "c++-module", "--precompile"),
                Command("provider.cppm", "build/.gens/two/shared.pcm", "-x", "c++-module", "--precompile"),
                Command("consumer.cpp", "build/.objs/app/consumer.o", "-c"),
            }));
            XmakeTarget first = new("one", "static", script, null, [provider], [], [], new([], [], null));
            XmakeTarget executable = new("app", "binary", script, null, [consumer], [], [], new([], [], null));
            return Task.FromResult(new XmakeWorkspace(folder, new("xmake", new(3, 1, 2), "3.1.2"),
                new("ambiguous", "debug", "mingw", "x86_64", Path.Combine(folder, "build"), script, "llvm"),
                [first, first with { Name = "two" }, executable], new(["app"], ImmutableDictionary<string, ImmutableArray<string>>.Empty.Add("app", ["one", "two"])),
                [], [script], new(database, database), null));
        }

        object Command(string file, string output, params string[] flags) => new
        {
            directory = root, file, arguments = (string[])[compiler, "-std=c++23", .. flags, "-o", output, file], output,
        };
    }

    [AvaloniaFact]
    public void Cancelling_a_module_request_withdraws_the_real_MCP_admission_prompt()
    {
        string folder = TestFolders.Create("mcp-modules-cancel-");
        DeleteAtEnd(folder);
        MainWindow window = new() { AgentAnswerLimit = TimeSpan.FromMinutes(5), StartsLanguageServer = false };
        window.Show();
        Ui.Await(window.OpenFolder(folder), "open the folder for MCP cancellation");
        McpClient client = Await(Waiting.Apart(() => Start(["--mcp-attach", ProcessId])), "attach the MCP client for cancellation");
        try
        {
            using CancellationTokenSource stop = new();
            Task<CallToolResult> request = Waiting.Apart(() => CallGivingUp(client, McpTools.ModuleWorkspace, new(), stop.Token));
            WaitFor(() => window.OwnedWindows.OfType<AgentAdmissionDialog>().Any(dialog => dialog.IsVisible), "the MCP admission prompt");
            stop.Cancel();
            WaitFor(() => request.IsCanceled && !window.OwnedWindows.OfType<AgentAdmissionDialog>().Any(dialog => dialog.IsVisible),
                "cancellation to withdraw the admission prompt");
        }
        finally
        {
            Gone(client);
            window.Close();
        }
    }
}

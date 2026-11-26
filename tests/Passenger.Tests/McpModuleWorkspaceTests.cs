using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;
using Passenger.App.Mcp;
using Passenger.Core;
using Passenger.Tests.Debugging.Helpers;
using Pitstop.Modules;
using Pitstop.Xmake;

namespace Passenger.Tests;

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

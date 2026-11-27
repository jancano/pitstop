using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Pitstop.App;
using Pitstop.App.Mcp;
using Pitstop.Core;
using Pitstop.Xmake;

namespace Pitstop.Tests;

public sealed class McpNativeProductTests : McpRig
{
    [Fact]
    public async Task Default_stdio_lists_native_tools_and_holds_Xmake_behind_workspace_trust()
    {
        string root = TestFolders.Create("native-mcp-");
        DeleteAtEnd(root);
        TestFolders.Write(root, "xmake.lua", "error(\"this project must not be evaluated\")");
        TestFolders.Write(root, "Managed.csproj", TestProjects.Console());
        await using McpClient client = await Start(["--mcp", root], environment: new()
        {
            [McpHost.ManagedVerificationVariable] = null,
            [XmakeService.ExecutableVariable] = Path.Combine(root, "absent-xmake"),
        });
        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Cancel);
        Assert.Contains(tools, tool => tool.Name == McpTools.ModuleWorkspace);
        Assert.Contains(tools, tool => tool.Name == McpTools.Build);
        Assert.DoesNotContain(tools, tool => tool.Name is McpTools.DebugStart or McpTools.NugetInstall or McpTools.TestsRun);
        CallToolResult managedAction = await Result(client, McpTools.IdeAction, new()
        {
            ["action"] = AppActions.SaveAsLaunchProfile,
        });
        Assert.True(managedAction.IsError);
        Assert.Contains("managed development profile", Text(managedAction), StringComparison.Ordinal);
        string status = Text(await Result(client, McpTools.ModuleWorkspace));
        Assert.Contains("trusted Xmake workspace", status, StringComparison.Ordinal);
        Assert.DoesNotContain("this project must not be evaluated", status, StringComparison.Ordinal);

        string trust = Path.Combine(OwnState, "trust.json");
        AtomicFile.Write(trust, JsonSerializer.SerializeToUtf8Bytes(new { version = 1, trusted = new[] { PathNames.Canonical(root) } }));
        await WaitForStatus(XmakeService.ExecutableVariable);
        File.Delete(trust);
        await WaitForStatus("trusted Xmake workspace");

        async Task WaitForStatus(string expected)
        {
            string answer = "";
            await Waiting.UntilAsync(async () => (answer = Text(await Result(client, McpTools.ModuleWorkspace))).Contains(expected, StringComparison.Ordinal),
                TimeSpan.FromSeconds(20), Cancel);
            Assert.Contains(expected, answer, StringComparison.Ordinal);
        }
    }
}

using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using Pitstop.App;
using Pitstop.App.Debugging;
using Pitstop.App.Mcp;
using Pitstop.Core;

namespace Pitstop.Tests.Debugging;

public sealed class McpNativeDebuggerTests : McpRig
{
    [AvaloniaFact]
    public void An_attached_MCP_client_controls_and_inspects_the_same_real_LLDB_session()
    {
        string root = NativeDebuggerIdeTests.Configure("native-debug");
        using IDisposable asked = WorkspaceTrust.Ask(root);
        WorkspaceTrust.Trust(root);
        MainWindow window = AgentsWindow(root, languageServer: false);
        McpClient client = Await(Waiting.Apart(() => Start(["--mcp-attach", ProcessId])), "attach the native debugger MCP client");
        try
        {
            WaitFor(() => window.Session.Xmake?.Snapshot is not null, "the native workspace to evaluate");
            Assert.True(window.SelectConfiguration("Xmake: debug-fixture"));
            int line = File.ReadLines(Path.Combine(root, "main.cpp")).TakeWhile(text => !text.Contains("// locals-stop", StringComparison.Ordinal)).Count() + 1;
            Task<JsonElement> setting = Waiting.Apart(() => Call(client, McpTools.NativeDebugControl, new()
            {
                ["operation"] = "set_breakpoint", ["path"] = "main.cpp", ["line"] = line, ["condition"] = "seed == 11",
            }));
            Window permission = Question(window, "permission to change debugger state");
            Assert.Contains($"Allow {McpTools.NativeDebugControl}?", Texts(permission));
            Press(permission, AgentToolDialog.AllowForSession);
            Assert.Equal("set", Await(setting, "set a real conditional source breakpoint").GetProperty("status").GetString());
            JsonElement started = Control("start");
            Assert.True(started.TryGetProperty("sessionId", out _), started.ToString());
            WaitFor(() => window.Session.Debug.Stop is { }, "the real conditional breakpoint in the UI");
            JsonElement state = Inspect("state");
            Assert.Equal("Paused", state.GetProperty("state").GetString());
            Assert.Equal(window.Session.Debug.Stop!.Thread, state.GetProperty("stoppedThread").GetInt32());
            JsonElement frames = Inspect("frames").GetProperty("frames");
            Assert.Contains(frames.EnumerateArray(), frame =>
                frame.EnumerateObject().Any(property => property.Name.Equals("name", StringComparison.OrdinalIgnoreCase)
                    && property.Value.GetString()?.Contains("inspect", StringComparison.Ordinal) == true));
            JsonElement evaluated = Control("evaluate", new() { ["expression"] = "seed + 2" });
            Assert.Equal("13", evaluated.GetProperty("value").GetString());
            JsonElement resisted = Control("start");
            Assert.Contains("active native debug session", resisted.GetProperty("status").GetString(), StringComparison.Ordinal);
            Assert.Equal(state.GetProperty("sessionId").GetString(), Inspect("state").GetProperty("sessionId").GetString());
            JsonElement stopped = Control("stop");
            Assert.Equal("Ended", stopped.GetProperty("state").GetString());
            Assert.False(window.Session.Debug.IsDebugging);
        }
        finally
        {
            Gone(client);
            window.Close();
            WorkspaceTrust.TakeBack(root);
        }

        JsonElement Control(string operation, Dictionary<string, object?>? arguments = null) =>
            Unasked(window, client, McpTools.NativeDebugControl, Arguments(operation, arguments));

        JsonElement Inspect(string operation) => Unasked(window, client, McpTools.NativeDebugInspect, Arguments(operation, null));

        static Dictionary<string, object?> Arguments(string operation, Dictionary<string, object?>? arguments)
        {
            arguments ??= [];
            arguments["operation"] = operation;
            return arguments;
        }
    }
}

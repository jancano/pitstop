using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Lsp;
using Passenger.Text;
using Pitstop.Cpp;
using Pitstop.Xmake;

namespace Passenger.Tests;

public sealed class CppLanguageLifecycleTests
{
    [AvaloniaFact]
    public void Clangd_replays_unsaved_documents_and_recovers_from_one_crash()
    {
        string root = Workspace();
        string dotnet = PathNames.Canonical(TestFolders.Create("cpp-to-dotnet-"));
        TestFolders.Write(dotnet, "App.csproj", TestProjects.Console());
        TestFolders.Write(dotnet, "App.slnx", "<Solution><Project Path=\"App.csproj\" /></Solution>");
        ConcurrentQueue<FakeClangd> starts = new();
        MainWindow window = new()
        {
            StartsLanguageServer = false, Autosaves = false, EvaluateXmake = Evaluate,
            StartClangd = async (folder, _, log, cancel) =>
            {
                FakeClangd fake = new();
                starts.Enqueue(fake);
                return await fake.StartAsync(folder, log, cancel);
            },
        };
        using IDisposable asking = WorkspaceTrust.Ask(root);
        window.Show();
        try
        {
            Ui.Await(window.OpenFolder(root), "the native workspace to open");
            Ui.WaitFor(() => window.AskingTrust is not null, () => "the trust question");
            Ui.ClickButton(window.AskingTrust!, WorkspaceTrustDialog.Untrusted);
            Assert.Null(window.OpenFile(Path.Combine(root, "main.cpp")));
            OpenDocument document = window.EditorArea.Active!;
            Assert.Empty(starts);
            Ui.ClickButton(window, window.Actions[AppActions.TrustFolder]!.Title);
            Ui.WaitFor(() => window.Session.Cpp?.For(document) is { HasDiagnostics: true }, () => window.LanguageShown ?? "the fake clangd");
            Assert.True(starts.TryPeek(out FakeClangd? first));
            Assert.Contains(LspMethods.Initialize, first.Calls);
            Assert.StartsWith("int main", first.Opened, StringComparison.Ordinal);

            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(0, 0, "// unsaved\n")]);
            Ui.WaitFor(() => first.Calls.Contains(LspMethods.DidChange), () => "a change to reach the first server");
            LspDocument obsolete = window.Session.Cpp!.For(document)!.Lsp!;
            first.Crash();
            Ui.WaitFor(() => window.Session.Cpp?.For(document) is { HasDiagnostics: true, Lsp: { } active } && active != obsolete,
                () => window.LanguageShown ?? "recovery of clangd");
            Assert.Equal(2, starts.Count);
            FakeClangd recovered = starts.Last();
            Assert.StartsWith("// unsaved", recovered.Opened, StringComparison.Ordinal);
            Assert.DoesNotContain("// unsaved", File.ReadAllText(document.Path), StringComparison.Ordinal);
            Assert.ThrowsAny<OperationCanceledException>(() => Ui.Await(obsolete.HoverAsync(0), "the obsolete request to be rejected"));
            recovered.Crash();
            Ui.WaitFor(() => window.Session.Cpp?.State?.StartsWith("clangd stopped", StringComparison.Ordinal) == true,
                () => window.LanguageShown ?? "the bounded recovery message");
            Assert.Equal(2, starts.Count);

            TestFolders.Write(root, ".xmake/test/xmake.conf", "release");
            Ui.WaitFor(() => starts.Count == 3 && window.Session.Cpp?.For(document) is { HasDiagnostics: true }
                && window.Session.Xmake?.Snapshot?.Configuration.Mode == "release",
                () => "clangd to follow the new Xmake snapshot");
            FakeClangd configured = starts.Last();
            Assert.StartsWith("// unsaved", configured.Opened, StringComparison.Ordinal);
            LspDocument last = window.Session.Cpp!.For(document)!.Lsp!;
            document.Save();
            Ui.Await(window.OpenFolder(dotnet), "the .NET workspace to replace clangd");
            Ui.WaitFor(() => configured.Calls.Contains(LspMethods.Shutdown), () => "clangd shutdown");
            Assert.Null(window.Session.Cpp?.Server);
            Assert.Single(window.Projects);
            Assert.ThrowsAny<OperationCanceledException>(() => Ui.Await(last.HoverAsync(0), "the previous workspace's request to be rejected"));
        }
        finally
        {
            window.Close();
            WorkspaceTrust.TakeBack(root);
            foreach (FakeClangd fake in starts)
            {
                fake.Crash();
            }
        }
    }

    [AvaloniaFact]
    public void Missing_clangd_leaves_lexical_editing_and_reports_the_configuration_variable()
    {
        string root = Workspace();
        MainWindow window = new()
        {
            StartsLanguageServer = false, Autosaves = false, EvaluateXmake = Evaluate,
            StartClangd = (_, _, _, _) => Task.FromException<LanguageSession>(
                new ClangdException($"Install clangd or set {Clangd.ExecutableVariable}.")),
        };
        window.Show();
        try
        {
            Ui.Await(window.OpenFolder(root), "the workspace to open");
            Assert.Null(window.OpenFile(Path.Combine(root, "main.cpp")));
            Ui.WaitFor(() => window.Session.Cpp?.State?.Contains(Clangd.ExecutableVariable, StringComparison.Ordinal) == true,
                () => "the missing clangd explanation");
            Assert.NotNull(window.EditorArea.Active!.Highlighter);
        }
        finally
        {
            window.Close();
        }
    }

    private static string Workspace()
    {
        string root = PathNames.Canonical(TestFolders.Create("cpp-lifecycle-"));
        TestFolders.Write(root, XmakeService.ProjectFile, "-- test workspace\n");
        TestFolders.Write(root, "main.cpp", "int main() { return 0; }\n");
        TestFolders.Write(root, ".xmake/test/xmake.conf", "debug");
        return root;
    }

    private static Task<XmakeWorkspace> Evaluate(string root, string output, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        Directory.CreateDirectory(output);
        string file = Path.Combine(root, "main.cpp");
        string database = Path.Combine(output, "compile_commands.json");
        File.WriteAllText(database, JsonSerializer.Serialize(new[]
        {
            new { file, directory = root, arguments = new[] { "clang++", "-c", file } },
        }));
        XmakeTarget target = new("test", "binary", Path.Combine(root, XmakeService.ProjectFile), null,
            [file], [], [], new(["llvm"], [], null));
        string config = Path.Combine(root, ".xmake", "test", "xmake.conf");
        return Task.FromResult(new XmakeWorkspace(root, new("xmake", new Version(3, 1, 2), "3.1.2"),
            new("test", File.ReadAllText(config), "mingw", "x86_64", Path.Combine(root, "build"), config, "llvm"),
            [target], new(["test"], ImmutableDictionary<string, ImmutableArray<string>>.Empty), [], [target.Script],
            new(database, database), null));
    }

    private sealed class FakeClangd
    {
        private TcpClient? client;
        public ConcurrentQueue<string> Calls { get; } = new();
        public string? Opened { get; private set; }

        public async Task<LanguageSession> StartAsync(string folder, string log, CancellationToken cancel)
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<LanguageSession> connected = LanguageSession.StartAsync(new ServerStart(null, folder, log, ClientCapabilities.Web) { Port = port }, cancel);
            TcpClient accepted = await listener.AcceptTcpClientAsync(cancel);
            client = accepted;
            UnawaitedWork.Observe(ServeAsync(accepted));
            return await connected;
        }

        public void Crash() => client?.Dispose();

        private async Task ServeAsync(TcpClient accepted)
        {
            using (accepted)
            {
                try
                {
                    NetworkStream stream = accepted.GetStream();
                    while (true)
                    {
                        (_, string body) = await LanguageServerPortTests.ReadAsync(stream);
                        using JsonDocument json = JsonDocument.Parse(body);
                        JsonElement request = json.RootElement;
                        string method = request.GetProperty("method").GetString()!;
                        Calls.Enqueue(method);
                        if (method == LspMethods.DidOpen)
                        {
                            JsonElement document = request.GetProperty("params").GetProperty("textDocument");
                            Opened = document.GetProperty("text").GetString();
                            await LanguageServerPortTests.WriteAsync(stream, JsonSerializer.Serialize(new
                            {
                                jsonrpc = "2.0", method = LspMethods.PublishDiagnostics,
                                @params = new { uri = document.GetProperty("uri"), version = document.GetProperty("version"), diagnostics = Array.Empty<object>() },
                            }));
                        }
                        if (method == LspMethods.Exit)
                        {
                            return;
                        }
                        if (request.TryGetProperty("id", out JsonElement id))
                        {
                            object? result = method == LspMethods.Initialize ? new { capabilities = new { textDocumentSync = 1, hoverProvider = true } } : null;
                            await LanguageServerPortTests.WriteAsync(stream, JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
                        }
                    }
                }
                catch (Exception problem) when (problem is IOException or SocketException or ObjectDisposedException)
                {
                }
            }
        }
    }
}

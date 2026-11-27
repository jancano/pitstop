using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Pitstop.Core;
using Pitstop.Lsp;

namespace Pitstop.CppProbe;

internal static class ClangdChecks
{
    internal static string AddExport(string provider) => provider + "\nexport constexpr int fresh_value() { return 42; }\n";

    internal static string UseNewExport(string consumer) => consumer.Replace("module_value()", "fresh_value()", StringComparison.Ordinal);

    internal static async Task RunAsync(ProcessStartInfo command, string folder, string logs, string fixture, bool edits, CancellationToken cancel)
    {
        JsonElement capabilities = JsonSerializer.SerializeToElement(new
        {
            textDocument = new { publishDiagnostics = new { versionSupport = true } },
            workspace = new { workspaceFolders = true },
        });
        await using LanguageServer server = await LanguageServer.StartAsync(new ServerStart(command, folder, logs, capabilities), cancel);
        ConcurrentDictionary<string, PublishDiagnosticsParams> diagnostics = new(StringComparer.OrdinalIgnoreCase);
        server.DiagnosticsPublished += (_, said) => diagnostics[said.Uri] = said;

        string mainPath = Path.Combine(folder, "main.cpp");
        string uri = new Uri(mainPath).AbsoluteUri;
        string main = File.ReadAllText(mainPath);
        await OpenAsync(server, uri, main);
        PublishDiagnosticsParams? initial = await DiagnosticsAsync(1);
        string symbol = fixture switch
        {
            "headers" => "header_answer",
            "modules" when main.Contains("fresh_value()", StringComparison.Ordinal) => "fresh_value",
            "modules" => "module_value",
            _ => "vector",
        };
        JsonElement completion = await RequestAsync("completion", symbol, symbol.Length - 1);
        JsonElement hover = await RequestAsync("hover", symbol, 2);
        JsonElement definition = await RequestAsync("definition", symbol, 2);
        JsonElement references = await RequestAsync("references", symbol, 2);
        object baseline = new
        {
            noErrors = Clean(initial),
            diagnostics = initial,
            symbol,
            completionContainsSymbol = Completions(completion, symbol),
            completion,
            hoverContainsSymbol = Contains(hover, symbol),
            hover,
            definitionMatches = Contains(definition, fixture switch { "headers" => "answer.hpp", "modules" => "arithmetic.cppm", _ => "vector" }),
            definition,
            referencesIncludeConsumer = Contains(references, "main.cpp"),
            referencesIncludeProvider = fixture == "modules" && Contains(references, "arithmetic.cppm"),
            references,
        };
        Program.WriteJson(Path.Combine(logs, "baseline.json"), baseline);

        if (!edits)
        {
            return;
        }

        // Keep the compiler outputs in place. Each edit is sent to the same running server.
        JsonElement partition = await RequestAsync("definition", "base_number", 2);
        JsonElement implementation = default;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            implementation = await RequestAsync("definition", "answer", 2);
            if (Contains(implementation, "implementation.cpp"))
            {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancel);
        }
        Program.WriteJson(Path.Combine(logs, "cross-module.json"), new
        {
            partitionResolved = Contains(partition, "numbers.cppm"), partition,
            implementationResolved = Contains(implementation, "implementation.cpp"), implementation,
        });

        string providerPath = Path.Combine(folder, "arithmetic.cppm");
        string providerUri = new Uri(providerPath).AbsoluteUri;
        string provider = File.ReadAllText(providerPath);
        await OpenAsync(server, providerUri, provider);
        main = UseNewExport(main);
        await ChangeAsync(server, uri, main, 2);
        PublishDiagnosticsParams? before = await DiagnosticsAsync(2);
        string updated = AddExport(provider);
        await ChangeAsync(server, providerUri, updated, 2);
        await ChangeAsync(server, uri, main + "\n", 3);
        PublishDiagnosticsParams? unsaved = await DiagnosticsAsync(3);
        JsonElement unsavedDefinition = await RequestAsync("definition", "fresh_value", 2);

        PublishDiagnosticsParams? saved;
        JsonElement savedDefinition;
        try
        {
            Program.Write(providerPath, updated);
            await server.NotifyAsync(LspMethods.DidSave, new { textDocument = new { uri = providerUri } });
            await server.NotifyAsync(LspMethods.DidChangeWatchedFiles, new { changes = new[] { new { uri = providerUri, type = 2 } } });
            await ChangeAsync(server, uri, main + "\n\n", 4);
            saved = await DiagnosticsAsync(4);
            savedDefinition = await RequestAsync("definition", "fresh_value", 2);
        }
        finally
        {
            Program.Write(providerPath, provider);
        }
        Program.WriteJson(Path.Combine(logs, "interface-edits.json"), new
        {
            before, unsaved, unsavedDefinition, saved, savedDefinition,
            introducedErrorObserved = before?.Diagnostics.Any(d => d.Message.Contains("fresh_value", StringComparison.Ordinal)) == true,
            unsavedResolved = Clean(unsaved) && Contains(unsavedDefinition, "arithmetic.cppm"),
            savedResolved = Clean(saved) && Contains(savedDefinition, "arithmetic.cppm"),
        });

        async Task<PublishDiagnosticsParams?> DiagnosticsAsync(int version)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                if (diagnostics.TryGetValue(uri, out PublishDiagnosticsParams? said) && said.Version == version)
                {
                    return said;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancel);
            }
            return null;
        }

        async Task<JsonElement> RequestAsync(string method, string word, int offset)
        {
            int start = main.IndexOf(word, StringComparison.Ordinal);
            if (start < 0)
            {
                throw new InvalidOperationException($"The fixture is missing the LSP request symbol {word}.");
            }
            int at = start + offset;
            string before = main[..at];
            int line = before.Count(c => c == '\n');
            int character = at - before.LastIndexOf('\n') - 1;
            JsonElement answer = await server.RequestAsync<JsonElement>($"textDocument/{method}", new
            {
                textDocument = new { uri },
                position = new { line, character },
                context = method == "references" ? new { includeDeclaration = true } : null,
            }, cancel);
            return answer.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement<object?>(null) : answer;
        }
    }

    private static bool Clean(PublishDiagnosticsParams? said) =>
        said is not null && !said.Diagnostics.Any(d => d.Severity is DiagnosticSeverity.Error or null);

    private static bool Contains(JsonElement response, string text) =>
        response.ValueKind != JsonValueKind.Undefined && response.GetRawText().Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool Completions(JsonElement response, string symbol)
    {
        JsonElement items = JsonMembers.Value(response, "items") ?? response;
        return items.ValueKind == JsonValueKind.Array && items.EnumerateArray().Any(item =>
            JsonMembers.Text(item, "label")?.TrimStart().StartsWith(symbol, StringComparison.Ordinal) == true);
    }

    private static Task OpenAsync(LanguageServer server, string uri, string text) => server.NotifyAsync(LspMethods.DidOpen, new
    {
        textDocument = new { uri, languageId = "cpp", version = 1, text },
    });

    private static Task ChangeAsync(LanguageServer server, string uri, string text, int version) => server.NotifyAsync(LspMethods.DidChange, new
    {
        textDocument = new { uri, version },
        contentChanges = new[] { new { text } },
    });
}

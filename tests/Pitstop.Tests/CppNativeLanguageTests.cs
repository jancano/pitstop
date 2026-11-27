using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Pitstop.App;
using Pitstop.App.ToolWindows;
using Pitstop.App.Workspace;
using Pitstop.Core;
using Pitstop.Editor;
using Pitstop.Lsp;
using Pitstop.Tests.Debugging.Helpers;
using Pitstop.Text;
using Pitstop.Cpp;
using Pitstop.Xmake;

namespace Pitstop.Tests;

public sealed class CppNativeLanguageTests
{
    [AvaloniaFact]
    public void Cxx23_modules_complete_navigate_and_refresh() => CheckModules("cxx23", capture: true);

    [AvaloniaFact]
    public void Cxx26_modules_complete_navigate_and_refresh() => CheckModules("cxx26", capture: false);

    [AvaloniaFact]
    public void Standard_library_modules_resolve_in_both_language_modes()
    {
        foreach (string standard in new[] { "cxx23", "cxx26" })
        {
            (string folder, _) = Configure("standard-library", standard);
            MainWindow window = Ui.OpenFolder(folder, languageServer: false, autosaves: false);
            try
            {
                OpenDocument consumer = Open(window, "main.cpp");
                LspDocument lsp = Ready(window, consumer);
                Assert.DoesNotContain(window.Session.Cpp!.For(consumer)!.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
                IReadOnlyList<LspLocation> definitions = Ui.Await(lsp.DefinitionAsync(At(consumer, "vector")), "the libc++ vector definition");
                Assert.Contains(definitions, place => place.Uri.Contains("vector", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Clangd_answers_editor_protocol_features_in_a_header_based_target()
    {
        (string folder, _) = Configure("headers", "cxx23");
        TestFolders.Write(folder, "include/answer.hpp", """
            #pragma once
            /// Returns the example value.
            inline int fixture_answer(int value) { return value; }
            """);
        TestFolders.Write(folder, "main.cpp", "#include \"answer.hpp\"\nint main(){return fixture_answer(42);}\n");
        MainWindow window = Ui.OpenFolder(folder, languageServer: false, autosaves: false);
        try
        {
            OpenDocument consumer = Open(window, "main.cpp");
            LspDocument lsp = Ready(window, consumer);
            int call = At(consumer, "fixture_answer");
            Assert.DoesNotContain(window.Session.Cpp!.For(consumer)!.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            (CompletionList? completion, _) = Ui.Await(lsp.CompletionAsync(call + 7, null), "completion and its documentation");
            CompletionItem item = Assert.Single(completion!.Items, i => i.Label.Contains("fixture_answer", StringComparison.Ordinal));
            Assert.NotNull(item.Documentation);
            Assert.NotNull(Ui.Await(lsp.SignatureHelpAsync(At(consumer, "42")), "the function signature"));
            Assert.Contains(Ui.Await(lsp.DeclarationAsync(call), "the function declaration"), place => place.Uri.EndsWith("answer.hpp", StringComparison.Ordinal));
            Assert.Contains(Ui.Await(lsp.OutlineAsync(), "document symbols"), symbol => symbol.Name == "main");
            Assert.NotEmpty(Ui.Await(lsp.SemanticTokensAsync(), "semantic highlighting").Tokens!.Data);
            Assert.Contains(Ui.Await(lsp.InlayHintsAsync(), "parameter hints").Hints, hint => hint.Label.Contains("value", StringComparison.Ordinal));
            Assert.NotEmpty(Ui.Await(lsp.FormatAsync(consumer.Language.Indent, consumer.Conventions), "formatting edits"));
            LanguageServer server = window.Session.Cpp.Server!;
            Assert.Contains(Ui.Await(server.RequestAsync<List<SymbolInformation>>(LspMethods.WorkspaceSymbol,
                new { query = "fixture_answer" }), "workspace symbols")!, symbol => symbol.Name == "fixture_answer");
            JsonElement? rename = Ui.Await(lsp.RenameAsync(call, "new_fixture_answer"), "symbol rename edits");
            Assert.NotNull(rename);
            Assert.NotEmpty(Shapes.WorkspaceEdit(rename.Value));

            int semicolon = At(consumer, ";");
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(semicolon, 1, "")]);
            _ = Ready(window, consumer);
            IReadOnlyList<Diagnostic> diagnostics = window.Session.Cpp.For(consumer)!.Diagnostics;
            Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.NotEmpty(Ui.Await(lsp.CodeActionsAsync(new TextRange(0, consumer.Text.Snapshot.Length), diagnostics), "the missing semicolon's code action"));
        }
        finally
        {
            window.Close();
        }
    }

    private static void CheckModules(string standard, bool capture)
    {
        (string folder, string originalDatabase) = Configure("modules", standard);
        MainWindow window = Ui.OpenFolder(folder, width: 1240, height: 760, languageServer: false, autosaves: false);
        try
        {
            OpenDocument consumer = Open(window, "main.cpp");
            LspDocument original = Ready(window, consumer);
            Assert.DoesNotContain(window.Session.Cpp!.For(consumer)!.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.NotNull(Ui.Await(original.HoverAsync(At(consumer, "module_value")), "the exported symbol hover"));
            Assert.Contains(Ui.Await(original.ReferencesAsync(At(consumer, "module_value")), "references for the exported symbol"),
                place => place.Uri.EndsWith("main.cpp", StringComparison.Ordinal));
            Assert.Contains(Ui.Await(original.DefinitionAsync(At(consumer, "module_value")), "the module interface definition"),
                place => place.Uri.EndsWith("arithmetic.cppm", StringComparison.Ordinal));
            Assert.Contains(Ui.Await(original.DefinitionAsync(At(consumer, "base_number")), "the exported partition definition"),
                place => place.Uri.EndsWith("numbers.cppm", StringComparison.Ordinal));

            Place(window, consumer, "module_value", 7);
            Ui.Run(window, AppActions.Complete);
            Ui.WaitFor(() => window.Assist.CompletionLabels.Any(label => label.Contains("module_value", StringComparison.Ordinal)),
                () => "completion for the exported module symbol");
            if (capture)
            {
                Capture(window, "completion");
            }
            Ui.Press(window, "ESCAPE");

            Place(window, consumer, "base_number", 2);
            Assert.True(window.Perform(AppActions.GoToCppDefinition));
            Ui.WaitFor(() => window.EditorArea.Active?.Name == "numbers.cppm", () => "navigation into the exported partition");
            if (capture)
            {
                Capture(window, "partition");
            }

            OpenDocument implementation = Open(window, "implementation.cpp");
            LspDocument implementationServer = Ready(window, implementation);
            IReadOnlyList<LspLocation> internalLinks = Ui.Await(implementationServer.DefinitionAsync(At(implementation, "increment")), "navigation into the internal partition");
            Assert.Contains(internalLinks, place => place.Uri.EndsWith("detail.cppm", StringComparison.Ordinal));
            OpenDocument provider = Open(window, "arithmetic.cppm");
            _ = Ready(window, provider);
            Open(window, "main.cpp");
            int replace = At(consumer, "module_value");
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(replace, "module_value".Length, "fresh_value")]);
            string unsaved = consumer.Text.Snapshot.ToString();
            Ui.WaitFor(() => consumer.Markers.Any(m => m.Kind == MarkerKind.Error), () => "clangd to diagnose the undeclared export");
            if (capture)
            {
                window.DockHost.Show(ToolWindowIds.Problems);
                Capture(window, "diagnostics");
            }

            Open(window, "arithmetic.cppm");
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(provider.Text.Snapshot.Length, 0, "\nexport constexpr int fresh_value() { return 42; }\n")]);
            provider.Save();
            Ui.WaitFor(() => window.Session.Cpp?.For(consumer)?.Lsp is { } current && current != original
                && window.Session.Cpp.For(consumer)!.HasDiagnostics, () => window.LanguageShown ?? "clangd to replay the unsaved consumer");
            LspDocument renewed = window.Session.Cpp!.For(consumer)!.Lsp!;
            Assert.Equal(unsaved, consumer.Text.Snapshot.ToString());
            Assert.DoesNotContain("fresh_value", File.ReadAllText(consumer.Path), StringComparison.Ordinal);
            Assert.DoesNotContain(consumer.Markers, marker => marker.Kind == MarkerKind.Error);
            Assert.Contains(Ui.Await(renewed.DefinitionAsync(At(consumer, "fresh_value")), "navigation to the newly saved export"),
                place => place.Uri.EndsWith("arithmetic.cppm", StringComparison.Ordinal));
            Assert.Equal(originalDatabase, File.ReadAllText(Path.Combine(folder, "compile_commands.json")));
        }
        finally
        {
            window.Close();
        }
    }

    private static (string Folder, string Database) Configure(string fixture, string standard)
    {
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        string? xmake = Environment.GetEnvironmentVariable(XmakeService.ExecutableVariable);
        string? clangd = Environment.GetEnvironmentVariable(Clangd.ExecutableVariable);
        Assert.SkipWhen(!OperatingSystem.IsWindows() || llvm is null || xmake is null || clangd is null
            || !Directory.Exists(llvm) || !File.Exists(xmake) || !File.Exists(clangd),
            "The Windows x64 native test requires PITSTOP_XMAKE, PITSTOP_CLANGD and PITSTOP_TEST_LLVM_MINGW for the Phase 1 toolchain.");
        string root = PathNames.Canonical(TestFolders.Create("cpp-language-"));
        string source = Path.Combine(Debuggees.Root, "tests", "CppWorkspaces", fixture);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string copy = Path.Combine(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
        string script = Path.Combine(root, XmakeService.ProjectFile);
        File.WriteAllText(script, File.ReadAllText(script).Replace("cxx23", standard, StringComparison.Ordinal));
        const string existing = "user-owned compilation database";
        TestFolders.Write(root, "compile_commands.json", existing);
        System.Diagnostics.ProcessStartInfo start = ChildProcesses.Command(xmake!,
            ["f", "-m", "debug", "-p", "mingw", "--toolchain=llvm", $"--mingw={llvm}", $"--sdk={llvm}", "--require=no", "-y"], root);
        start.Environment["XMAKE_STATS"] = "false";
        ChildResult configured = Ui.Await(ChildProcesses.RunAsync(start, limit: TimeSpan.FromSeconds(60)), "LLVM-MinGW to configure");
        Assert.True(configured.Succeeded, configured.Output + configured.Error);
        return (root, existing);
    }

    private static OpenDocument Open(MainWindow window, string file)
    {
        Assert.Null(window.OpenFile(Path.Combine(window.Folder!, file)));
        return window.EditorArea.Active!;
    }

    private static LspDocument Ready(MainWindow window, OpenDocument document)
    {
        Ui.WaitFor(() => window.Session.Cpp?.For(document) is { Lsp: not null, HasDiagnostics: true },
            () => $"versioned clangd diagnostics for {document.Name} ({window.LanguageShown})");
        return window.Session.Cpp!.For(document)!.Lsp!;
    }

    private static int At(OpenDocument document, string name) => document.Text.Snapshot.ToString().IndexOf(name, StringComparison.Ordinal);

    private static void Place(MainWindow window, OpenDocument document, string name, int after) =>
        window.EditorArea.CodeEditor.GoTo(document.Text.Snapshot.GetPosition(At(document, name) + after));

    private static void Capture(MainWindow window, string state)
    {
        if (Environment.GetEnvironmentVariable("PITSTOP_TEST_CPP_SHOTS") is not { Length: > 0 } directory)
        {
            return;
        }
        Directory.CreateDirectory(directory);
        ThemeVariant? previous = window.RequestedThemeVariant;
        try
        {
            foreach ((string name, ThemeVariant theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
            {
                window.RequestedThemeVariant = theme;
                Ui.Settle();
                using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No editor frame was drawn.");
                frame.Save(Path.Combine(directory, $"cpp-{state}-{name}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.RequestedThemeVariant = previous;
        }
    }
}

using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Pitstop.App;
using Pitstop.App.Workspace;
using Pitstop.Lsp;
using Pitstop.Text;
using Pitstop.Modules;
using Pitstop.Xmake;

namespace Pitstop.Tests;

public sealed class ModuleScaleTests
{
    [AvaloniaFact]
    public void Measures_the_editor_on_a_generated_module_workspace()
    {
        string? folder = Environment.GetEnvironmentVariable("PITSTOP_TEST_MODULE_SCALE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(folder), "Set PITSTOP_TEST_MODULE_SCALE to a disposable, configured workspace from tools/module-scale.py.");
        string root = Path.GetFullPath(folder!);
        Assert.Equal("set_project(\"pitstop-module-scale\")", File.ReadLines(Path.Combine(root, "xmake.lua")).First());
        TimeSpan limit = TimeSpan.FromMinutes(2);
        Dictionary<string, object> measurements = [];
        string main = Path.Combine(root, "main.cpp");
        string imported = File.ReadLines(main).First()["import ".Length..].TrimEnd(';');
        MainWindow window = new() { StartsLanguageServer = false, Autosaves = false };
        window.Show();
        try
        {
            long began = Stopwatch.GetTimestamp();
            Task opened = window.OpenFolder(root);
            Ui.PumpUntil(() => window.Session.Xmake?.Snapshot is not null, "the generated Xmake workspace", limit);
            Mark("workspaceSeconds", began);
            XmakeWorkspace workspace = window.Session.Xmake!.Snapshot!;
            Assert.True(workspace.CompilationDatabases is not null, workspace.CompilationDatabaseProblem);
            long evaluated = Stopwatch.GetTimestamp();
            Ui.PumpUntil(() => window.Session.Modules?.Snapshot is not null, "the dependency scanner", limit);
            Mark("scanAndGraphSeconds", evaluated);
            ModuleWorkspace first = window.Session.Modules!.Snapshot!;
            Assert.True(first.Problem is null, first.Problem);
            measurements["providers"] = first.Providers.Length;
            measurements["resolvedEdges"] = first.Dependencies.Count(edge => edge.Provider is not null);
            Ui.Await(opened, "opening the generated workspace", limit);

            long opening = Stopwatch.GetTimestamp();
            Assert.Null(window.OpenFile(main));
            OpenDocument consumer = window.EditorArea.Active!;
            Ui.PumpUntil(() => window.Session.Cpp?.For(consumer) is { Lsp: not null, HasDiagnostics: true }, "the first clangd analysis", limit);
            Mark("firstAnalysisSeconds", opening);
            LspDocument original = window.Session.Cpp!.For(consumer)!.Lsp!;
            int serverId = window.Session.Cpp.Server!.ProcessId;
            MeasureMemory("editor", Environment.ProcessId);
            MeasureMemory("clangd", serverId);

            long input = Stopwatch.GetTimestamp();
            window.EditorArea.CodeEditor.GoTo(new TextPosition(0, "import scale.".Length));
            Mark("caretDispatchSeconds", input);
            long requesting = Stopwatch.GetTimestamp();
            Assert.True(window.Perform(AppActions.Complete));
            Ui.PumpUntil(() => window.Assist.CompletionLabels.Contains(imported), "import completion in the active target", limit);
            Mark("completionSeconds", requesting);
            Ui.Press(window, "ESCAPE");

            string providerPath = first.Named(imported).Single(provider => provider.Origin == ModuleOrigin.Workspace).Unit.Source;
            Assert.Null(window.OpenFile(providerPath));
            OpenDocument provider = window.EditorArea.Active!;
            string disk = File.ReadAllText(providerPath);
            try
            {
                window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(provider.Text.Snapshot.Length, 0, "\nexport int benchmark_saved();\n")]);
                long saving = Stopwatch.GetTimestamp();
                provider.Save();
                Ui.PumpUntil(() => window.Session.Modules?.Snapshot is { } refreshed && refreshed != first && refreshed.Problem is null,
                    "the saved interface's module graph", limit);
                Mark("savedGraphSeconds", saving);
                Ui.PumpUntil(() => window.Session.Cpp?.For(consumer) is { HasDiagnostics: true, Lsp: { } current } && current != original,
                    "clangd to replay the consumer", limit);
                Mark("savedAnalysisSeconds", saving);
            }
            finally
            {
                File.WriteAllText(providerPath, disk);
            }
        }
        finally
        {
            window.Close();
            string json = JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(root, "pitstop-measurements.json"), json + "\n");
        }

        void Mark(string name, long start) => measurements[name] = Stopwatch.GetElapsedTime(start).TotalSeconds;

        void MeasureMemory(string name, int id)
        {
            using Process process = Process.GetProcessById(id);
            measurements[$"{name}WorkingSetBytes"] = process.WorkingSet64;
            measurements[$"{name}PeakWorkingSetBytes"] = process.PeakWorkingSet64;
        }
    }
}

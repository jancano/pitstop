using System.Collections.Immutable;
using System.Text.Json;
using Passenger.Core;
using Passenger.Lsp;
using Pitstop.Xmake;

namespace Pitstop.Cpp;

/// <summary>Compilation contexts in Xmake's semantic database, in the same order in which clangd sees them.</summary>
public sealed class CppCompilation
{
    private readonly ImmutableDictionary<string, ImmutableArray<CppCommand>> commands;
    private readonly ImmutableDictionary<string, ImmutableArray<XmakeTarget>> owners;
    private readonly string database;

    private CppCompilation(XmakeWorkspace workspace, string database, IEnumerable<CppCommand> entries)
    {
        Workspace = workspace;
        this.database = database;
        commands = entries.GroupBy(c => c.File, PathNames.Comparer)
            .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), PathNames.Comparer);
        owners = workspace.Targets.SelectMany(t => t.SourceFiles.Concat(t.HeaderFiles).Distinct(PathNames.Comparer).Select(f => (File: f, Target: t)))
            .GroupBy(pair => pair.File, PathNames.Comparer)
            .ToImmutableDictionary(g => g.Key, g => g.Select(p => p.Target).OrderBy(t => t.Name, StringComparer.Ordinal).ToImmutableArray(), PathNames.Comparer);
    }

    public XmakeWorkspace Workspace { get; }

    public string Database => database;

    public ImmutableArray<CppCommand> CommandsFor(string path) => commands.GetValueOrDefault(path, []);

    public bool IsModuleInput(string path) => CommandsFor(path).Any(c => c.IsModule);

    public string HeaderLanguage(string path, string fallback)
    {
        string? named = CommandsFor(path).FirstOrDefault()?.ExplicitLanguage;
        if (named is not null)
        {
            return named.Contains("c++", StringComparison.Ordinal) ? LanguageIds.Cpp : LanguageIds.C;
        }
        string[] kinds = [.. owners.GetValueOrDefault(path, []).SelectMany(t => t.Toolchain.Compilers).Select(c => c.SourceKind).Distinct()];
        return kinds is ["cxx"] ? LanguageIds.Cpp : kinds is ["cc"] ? LanguageIds.C : fallback;
    }

    public string Context(string path)
    {
        ImmutableArray<XmakeTarget> targets = owners.GetValueOrDefault(path, []);
        ImmutableArray<CppCommand> rows = CommandsFor(path);
        if (targets.Length <= 1 || rows.IsEmpty)
        {
            return targets.Length == 1 ? targets[0].Name : "";
        }
        return rows[0].Target is { } active ? $"{active} (of {targets.Length} Xmake targets)" : "selected Xmake command";
    }

    public static async Task<CppCompilation> ReadAsync(XmakeWorkspace workspace, CancellationToken cancel)
    {
        if (workspace.CompilationDatabases is not { } database)
        {
            throw new ClangdException(workspace.CompilationDatabaseProblem ?? "Xmake has no semantic compilation database. Check the project configuration.");
        }
        await using FileStream input = File.OpenRead(database.Semantic);
        using JsonDocument json = await JsonDocument.ParseAsync(input, cancellationToken: cancel).ConfigureAwait(false);
        ILookup<string, string> membership = workspace.Targets.SelectMany(t => t.SourceFiles.Select(file => (file, t.Name)))
            .ToLookup(item => item.file, item => item.Name, PathNames.Comparer);
        List<(CppCommand Command, JsonElement Row)> entries = [];
        foreach (JsonElement row in json.RootElement.EnumerateArray())
        {
            cancel.ThrowIfCancellationRequested();
            if (JsonMembers.Text(row, "directory") is not { } directory || JsonMembers.Text(row, "file") is not { } file)
            {
                throw new ClangdException("An Xmake compilation command has no working directory or source file.");
            }
            string[] args = [.. JsonMembers.Texts(row, "arguments")];
            if (args.Length == 0)
            {
                throw new ClangdException($"Xmake generated no compiler arguments for {file}.");
            }
            int language = Array.IndexOf(args, "-x") + 1;
            string? kind = language > 0 && language < args.Length ? args[language] : null;
            string path = PathNames.Canonical(Path.GetFullPath(file, Path.GetFullPath(directory, workspace.Root)));
            string? output = JsonMembers.Text(row, "output");
            string[] candidates = [.. membership[path].Distinct(StringComparer.Ordinal)];
            string? target = candidates.Length == 1 ? candidates[0] : OutputTarget(output, candidates);
            entries.Add((new(path, output, kind,
                kind == "c++-module" || args.Contains("--precompile", StringComparer.Ordinal) || args.Contains("-emit-module-interface", StringComparer.Ordinal), target), row));
        }
        if (entries.Count == 0)
        {
            throw new ClangdException("Xmake's semantic compilation database is empty. Configure a C or C++ target in Xmake.");
        }
        // Keep Xmake's source-oriented database intact. Give clangd just one target's commands for a shared source.
        // Preserve compiler command order within a target, since Xmake may emit a BMI and an object command for one file.
        HashSet<string> shared = new(membership.Where(group => group.Distinct(StringComparer.Ordinal).Count() > 1).Select(group => group.Key), PathNames.Comparer);
        if (shared.Count == 0)
        {
            return new(workspace, database.Semantic, entries.Select(e => e.Command));
        }
        Dictionary<string, (string? Target, string? Output, string? Row)> selected = new(PathNames.Comparer);
        foreach (IGrouping<string, (CppCommand Command, JsonElement Row)> group in
            entries.Where(e => shared.Contains(e.Command.File)).GroupBy(e => e.Command.File, PathNames.Comparer))
        {
            (CppCommand Command, JsonElement Row) first = group.OrderBy(e => e.Command.Target is null).ThenBy(e => e.Command.Target, StringComparer.Ordinal)
                .ThenBy(e => e.Command.Output, StringComparer.Ordinal).ThenBy(e => e.Row.GetRawText(), StringComparer.Ordinal).First();
            selected[group.Key] = (first.Command.Target, first.Command.Output, first.Row.GetRawText());
        }
        entries.RemoveAll(entry => selected.TryGetValue(entry.Command.File, out (string? Target, string? Output, string? Row) active)
            && (active.Target is not null ? active.Target != entry.Command.Target
                : active.Output != entry.Command.Output || active.Row != entry.Row.GetRawText()));
        string pathToDatabase = Path.Combine(Path.GetDirectoryName(database.Semantic)!, "selected", "compile_commands.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pathToDatabase)!);
        cancel.ThrowIfCancellationRequested();
        AtomicFile.Write(pathToDatabase, JsonSerializer.SerializeToUtf8Bytes(entries.Select(e => e.Row)));
        return new(workspace, pathToDatabase, entries.Select(e => e.Command));
    }

    private static string? OutputTarget(string? output, string[] candidates)
    {
        if (output is null)
        {
            return null;
        }
        string[] parts = output.Split(['/', '\\']);
        for (int i = 0; i + 1 < parts.Length; i++)
        {
            if (parts[i] is ".objs" or ".gens" && candidates.Contains(parts[i + 1], StringComparer.Ordinal))
            {
                return parts[i + 1];
            }
        }
        return null;
    }
}

public sealed record CppCommand(string File, string? Output, string? ExplicitLanguage, bool IsModule, string? Target);

using System.Collections.Immutable;
using System.Text.Json;
using Pitstop.Core;
using Pitstop.Lsp;
using Pitstop.ProjectSystem;

namespace Pitstop.Cpp;

/// <summary>Compilation contexts in the provider's semantic database, in the same order in which clangd sees them.</summary>
public sealed class CppCompilation
{
    private readonly ImmutableDictionary<string, ImmutableArray<CppCommand>> commands;
    private readonly ImmutableDictionary<string, ImmutableArray<INativeTarget>> owners;
    private readonly string database;

    private CppCompilation(INativeWorkspace workspace, string database, IEnumerable<CppCommand> entries)
    {
        Workspace = workspace;
        this.database = database;
        commands = entries.GroupBy(c => c.File, PathNames.Comparer)
            .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), PathNames.Comparer);
        owners = workspace.Targets.SelectMany(t => t.SourceFiles.Concat(t.HeaderFiles).Distinct(PathNames.Comparer).Select(f => (File: f, Target: t)))
            .GroupBy(pair => pair.File, PathNames.Comparer)
            .ToImmutableDictionary(g => g.Key, g => g.Select(p => p.Target).OrderBy(t => t.Name, StringComparer.Ordinal).ToImmutableArray(), PathNames.Comparer);
    }

    public INativeWorkspace Workspace { get; }

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
        NativeLanguage[] kinds = [.. owners.GetValueOrDefault(path, []).SelectMany(t => t.CompilerLanguages).Distinct()];
        return kinds is [NativeLanguage.Cpp] ? LanguageIds.Cpp : kinds is [NativeLanguage.C] ? LanguageIds.C : fallback;
    }

    public string Context(string path)
    {
        ImmutableArray<INativeTarget> targets = owners.GetValueOrDefault(path, []);
        ImmutableArray<CppCommand> rows = CommandsFor(path);
        if (targets.Length <= 1 || rows.IsEmpty)
        {
            return targets.Length == 1 ? targets[0].Name : "";
        }
        return rows[0].Target is { } active ? $"{active} (of {targets.Length} {Workspace.ProviderName} targets)" : $"selected {Workspace.ProviderName} command";
    }

    public static async Task<CppCompilation> ReadAsync(INativeWorkspace workspace, CancellationToken cancel)
    {
        string provider = workspace.ProviderName;
        if (workspace.CompilationDatabases is not { } database)
        {
            throw new ClangdException(workspace.CompilationDatabaseProblem ?? $"{provider} has no semantic compilation database. Check the project configuration.");
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
                throw new ClangdException($"A {provider} compilation command has no working directory or source file.");
            }
            string[] args = [.. JsonMembers.Texts(row, "arguments")];
            if (args.Length == 0)
            {
                throw new ClangdException($"{provider} generated no compiler arguments for {file}.");
            }
            int language = Array.IndexOf(args, "-x") + 1;
            string? kind = language > 0 && language < args.Length ? args[language] : null;
            string path = PathNames.Canonical(Path.GetFullPath(file, Path.GetFullPath(directory, workspace.Root)));
            string? output = JsonMembers.Text(row, "output");
            string[] candidates = [.. membership[path].Distinct(StringComparer.Ordinal)];
            string? target = candidates.Length == 1 ? candidates[0] : workspace.MatchOutputTarget(output, candidates);
            entries.Add((new(path, output, kind,
                kind == "c++-module" || args.Contains("--precompile", StringComparer.Ordinal) || args.Contains("-emit-module-interface", StringComparer.Ordinal), target), row));
        }
        if (entries.Count == 0)
        {
            throw new ClangdException($"{provider}'s semantic compilation database is empty. Configure a C or C++ target.");
        }
        // Give clangd one target's commands for a shared source while preserving BMI and object command order.
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

}

public sealed record CppCommand(string File, string? Output, string? ExplicitLanguage, bool IsModule, string? Target);

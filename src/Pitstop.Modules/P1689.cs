using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Passenger.Core;
using Pitstop.Xmake;

namespace Pitstop.Modules;

internal static class P1689
{
    internal sealed record Document(int Version, int Revision, ImmutableArray<Rule> Rules);
    internal sealed record Rule([property: JsonPropertyName("primary-output")] string Output,
        ImmutableArray<Entry> Provides, ImmutableArray<Entry> Requires);
    internal sealed record Entry([property: JsonPropertyName("logical-name")] string Name,
        [property: JsonPropertyName("source-path")] string? Source,
        [property: JsonPropertyName("is-interface")] bool? Interface,
        [property: JsonPropertyName("unique-on-source-path")] bool UniqueOnSourcePath,
        [property: JsonPropertyName("lookup-method")] string? LookupMethod);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    internal static ImmutableArray<Rule> Read(string json)
    {
        Document document = JsonSerializer.Deserialize<Document>(json, Json) ?? throw new JsonException("Missing P1689 document.");
        if (document.Version != 1 || document.Revision != 0 || document.Rules.IsDefault
            || document.Rules.Any(rule => rule is null || string.IsNullOrWhiteSpace(rule.Output)))
        {
            throw new JsonException("Expected P1689 version 1, revision 0 and rules with primary-output.");
        }
        return document.Rules;
    }

    internal static ModuleWorkspace Resolve(XmakeWorkspace workspace, IReadOnlyList<ScanCommand> commands, ImmutableArray<Rule> rules,
        IEnumerable<string> omitted, string? problem, CancellationToken stop)
    {
        ILookup<string, ScanCommand> outputs = commands.ToLookup(command => command.Unit.Output, PathNames.Comparer);
        ILookup<string, ScanCommand> rawOutputs = commands.ToLookup(command => command.Output, PathNames.Comparer);
        List<(ScanCommand Command, Rule Rule)> matched = [];
        List<string> missing = [.. omitted];
        foreach (Rule rule in rules)
        {
            stop.ThrowIfCancellationRequested();
            ScanCommand[] matches = [.. (Path.IsPathFullyQualified(rule.Output) ? outputs[Path.GetFullPath(rule.Output)] : rawOutputs[rule.Output])];
            if (matches is [ScanCommand command])
            {
                matched.Add((command, rule));
                if (Items(rule.Provides).Concat(Items(rule.Requires)).Any(entry => !Named(entry)))
                {
                    missing.Add($"Unsupported dependency kind in {command.Unit.Source}");
                }
            }
            else
            {
                missing.Add($"Unmatched scanner output {rule.Output}");
            }
        }
        HashSet<int> scanned = [.. matched.Select(pair => pair.Command.Unit.Id)];
        missing.AddRange(commands.Where(command => !scanned.Contains(command.Unit.Id)).Select(command => command.Unit.Source));
        ModuleProvider[] providers = [.. matched.SelectMany(pair => Items(pair.Rule.Provides).Where(Named).Select(entry => Provider(workspace, pair.Command, entry)))
            .GroupBy(provider => (provider.Identity, provider.Unit.Source, provider.Kind))
            .Select(group => group.OrderBy(provider => provider.Unit.Id).First())];
        ILookup<string, ModuleProvider> names = providers.ToLookup(provider => provider.Name, StringComparer.Ordinal);
        Dictionary<string, ImmutableHashSet<string>> visibility = commands.Select(command => command.Unit.Context.Target).OfType<string>()
            .Distinct(StringComparer.Ordinal).ToDictionary(target => target, target => ModuleWorkspace.VisibleTargets(workspace, target), StringComparer.Ordinal);
        List<ModuleDependency> edges = [];
        foreach ((ScanCommand command, Rule rule) in matched)
        {
            stop.ThrowIfCancellationRequested();
            foreach (Entry requirement in Items(rule.Requires).Where(Named))
            {
                TranslationUnit unit = command.Unit;
                string? source = requirement.Source is { Length: > 0 } path ? PathNames.Canonical(Path.GetFullPath(path, command.Directory)) : null;
                ModuleResolutionEvidence evidence;
                ImmutableArray<ModuleProvider> candidates;
                if (command.BuildModules.TryGetValue(requirement.Name, out string? bmi))
                {
                    candidates = [.. names[requirement.Name].Where(provider => PathNames.Same(provider.Unit.Output, bmi))];
                    evidence = ModuleResolutionEvidence.XmakeCommand;
                }
                else
                {
                    candidates = [.. names[requirement.Name].Where(provider =>
                        (source is null || PathNames.Same(provider.Unit.Source, source)) &&
                        (unit.Context.Target is not null && provider.Unit.Context.Target == unit.Context.Target || unit.Context.Target is { } target &&
                            provider.Unit.Context.Target is { } owner && visibility[target].Contains(owner) ||
                            source is not null && (unit.Context.Target is null || provider.Origin != ModuleOrigin.Workspace)))];
                    evidence = source is not null ? ModuleResolutionEvidence.ScannerSource : ModuleResolutionEvidence.TargetDependencies;
                }
                if (source is not null && candidates.Length > 1 && candidates.All(provider => provider.Origin != ModuleOrigin.Workspace))
                {
                    ImmutableArray<ModuleProvider> sameTarget = [.. candidates.Where(provider => provider.Unit.Context.Target == unit.Context.Target)];
                    if (!sameTarget.IsEmpty)
                    {
                        candidates = sameTarget;
                    }
                }
                edges.Add(new(unit, requirement.Name, source, candidates.Length switch
                {
                    0 => ModuleResolution.Unresolved,
                    1 => ModuleResolution.Resolved,
                    _ => ModuleResolution.Ambiguous,
                }, candidates) { Evidence = candidates.Length == 1 ? evidence : ModuleResolutionEvidence.Unavailable });
            }
        }
        return new(workspace, [.. commands.Select(command => command.Unit)], [.. providers.OrderBy(provider => provider.Name, StringComparer.Ordinal)],
            [.. edges], [.. missing.Distinct(PathNames.Comparer)], problem);
    }

    private static bool Named(Entry entry) => entry is not null && !string.IsNullOrWhiteSpace(entry.Name) && !entry.UniqueOnSourcePath
        && entry.LookupMethod is null or "by-name";

    private static ImmutableArray<Entry> Items(ImmutableArray<Entry> entries) => entries.IsDefault ? [] : entries;

    private static ModuleProvider Provider(XmakeWorkspace workspace, ScanCommand command, Entry entry)
    {
        bool partition = entry.Name.Contains(':');
        ModuleUnitKind kind = (partition, entry.Interface) switch
        {
            (true, true) => ModuleUnitKind.InterfacePartition,
            (true, false) => ModuleUnitKind.InternalPartition,
            (false, true) => ModuleUnitKind.PrimaryInterface,
            _ => ModuleUnitKind.Unknown,
        };
        string? sdk = Path.GetDirectoryName(Path.GetDirectoryName(command.Unit.Context.Compiler));
        ModuleOrigin origin = PathNames.Within(command.Unit.Source, workspace.Root) ? ModuleOrigin.Workspace
            : entry.Name is "std" or "std.compat" && sdk is not null && PathNames.Within(command.Unit.Source, Path.Combine(sdk, "share", "libc++"))
                ? ModuleOrigin.StandardLibrary : ModuleOrigin.External;
        return new(new(entry.Name, command.Unit.Context), command.Unit, kind, origin);
    }
}

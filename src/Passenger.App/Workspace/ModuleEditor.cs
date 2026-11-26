using System.Text.RegularExpressions;
using Passenger.App.CSharp;
using Passenger.Core;
using Passenger.Lsp;
using Passenger.Text;
using Pitstop.Modules;

namespace Passenger.App.Workspace;

internal static partial class ModuleEditor
{
    internal static CodeCompletion.OwnItems? Complete(ModuleWorkspace? snapshot, string path, string? activeTarget, TextSnapshot text, int caret)
    {
        int line = text.GetPosition(caret).Line;
        int start = text.GetLineStart(line);
        Match input = ImportPrefix().Match(text.GetLineText(line)[..(caret - start)]);
        if (!input.Success || snapshot is null || Units(snapshot, path, activeTarget).FirstOrDefault() is not { Context.Target: { } target })
        {
            return null;
        }
        Group prefix = input.Groups["name"];
        string? owner = Owner(snapshot, path, target);
        bool relative = prefix.Value.StartsWith(':');
        bool reexport = input.Groups["export"].Success;
        HashSet<ModuleIdentity> imported = [.. snapshot.Dependencies.Where(edge => edge.Consumer.Context.Target == target)
            .Select(edge => edge.Provider).OfType<ModuleProvider>().Select(provider => provider.Identity)];
        List<CompletionItem> items = [];
        foreach (IGrouping<string, ModuleProvider> group in snapshot.Providers.Where(provider => relative
            ? owner is not null && provider.PrimaryName == owner && provider.Unit.Context.Target == target
                && (provider.Kind == ModuleUnitKind.InterfacePartition || !reexport && provider.Kind == ModuleUnitKind.InternalPartition)
                && !PathNames.Same(provider.Unit.Source, path)
            : provider.Kind == ModuleUnitKind.PrimaryInterface && (provider.Unit.Context.Target == target || imported.Contains(provider.Identity)))
            .GroupBy(provider => provider.Name, StringComparer.Ordinal))
        {
            if (group.Take(2).Count() != 1)
            {
                continue;
            }
            ModuleProvider provider = group.First();
            string label = relative ? provider.Name[provider.PrimaryName.Length..] : provider.Name;
            items.Add(new CompletionItem { Label = label, Kind = CompletionItemKind.Module,
                Detail = $"C++ module · {provider.Unit.Context.Target ?? provider.Origin.ToString()}" });
        }
        return items.Count == 0 ? null : new CodeCompletion.OwnItems(start + prefix.Index, items, null) { WordCharacters = ".:" };
    }

    internal static ModuleProvider? ProviderAt(ModuleWorkspace snapshot, string path, string? target, TextSnapshot text, int caret)
    {
        Match input = ImportPrefix().Match(text.GetLineText(text.GetPosition(caret).Line).TrimEnd().TrimEnd(';'));
        if (input.Success && input.Groups["name"].Value is { Length: > 0 } name)
        {
            if (name.StartsWith(':'))
            {
                name = Owner(snapshot, path, target) + name;
            }
            ModuleProvider[] found = [.. Units(snapshot, path, target).SelectMany(snapshot.Imports).Where(edge => edge.Name == name)
                .Select(edge => edge.Provider).OfType<ModuleProvider>().Distinct().Take(2)];
            return found is [ModuleProvider provider] ? provider : null;
        }
        ModuleProvider[] own = [.. snapshot.Providers.Where(provider => PathNames.Same(provider.Unit.Source, path)
            && (target is null || provider.Unit.Context.Target == target)).Take(2)];
        return own is [ModuleProvider single] ? single : null;
    }

    private static IEnumerable<TranslationUnit> Units(ModuleWorkspace snapshot, string path, string? target)
    {
        TranslationUnit[] units = [.. snapshot.UnitsFor(path)];
        string? selected = target ?? units.Select(unit => unit.Context.Target).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).FirstOrDefault();
        return units.Where(unit => unit.Context.Target == selected);
    }

    private static string? Owner(ModuleWorkspace snapshot, string path, string? target)
    {
        string[] names = [.. snapshot.Providers.Where(provider => PathNames.Same(provider.Unit.Source, path)
            && (target is null || provider.Unit.Context.Target == target)).Select(provider => provider.PrimaryName).Distinct(StringComparer.Ordinal).Take(2)];
        return names is [string name] ? name : null;
    }

    // Editor input context only. Provider and dependency facts come from the scanner.
    [GeneratedRegex(@"^\s*(?<export>export\s+)?import\s+(?<name>[a-zA-Z0-9_.:]*)$")]
    private static partial Regex ImportPrefix();
}

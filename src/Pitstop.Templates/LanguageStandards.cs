using System.Collections.Immutable;
using System.Text.Json;

namespace Pitstop.Templates;

public sealed record LanguageStandard(string Id, string Label, string Language, int Revision, int Order, bool Recommended = false,
    bool Experimental = false, ImmutableArray<string> Capabilities = default);

public sealed record LanguageStandards(ImmutableArray<LanguageStandard> Entries)
{
    public static LanguageStandards Read(Stream source)
    {
        ImmutableArray<LanguageStandard> items = JsonSerializer.Deserialize<ImmutableArray<LanguageStandard>>(source, TemplateCatalog.Json);
        if (items.IsDefaultOrEmpty || items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Label)
            || string.IsNullOrWhiteSpace(item.Language) || item.Revision <= 0) || items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
        {
            throw new JsonException("Expected a catalog of standards with unique IDs, labels, languages and revisions.");
        }
        return new([.. items.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal)]);
    }

    public IEnumerable<LanguageStandard> Available(TemplateManifest template, IReadOnlySet<string> verifiedStandards) => Entries.Where(item =>
        item.Language == template.Language && item.Order >= (template.MinimumStandardOrder ?? 0) && verifiedStandards.Contains(item.Id));
}

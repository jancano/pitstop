using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pitstop.Templates;

public enum TemplateParameterKind { String, Boolean, Choice, LanguageStandard, Toolchain, Directory, Integer }
public enum TemplateValidation { None, FileName, TargetName, Identifier, ModuleName }

public sealed record TemplateCondition(string Parameter, string EqualsValue);

public sealed record TemplateParameter(string Id, string Label, TemplateParameterKind Type, JsonElement Default,
    bool Required = false, string? Help = null, ImmutableArray<string> Choices = default,
    TemplateValidation Validation = TemplateValidation.None, string? RequiresCapability = null, TemplateCondition? When = null,
    string? RequiresStandardCapability = null);

public sealed record TemplateFile(string Source, string Destination, bool Binary = false, TemplateCondition? When = null);

public sealed record TemplateManifest(int SchemaVersion, string Id, string Version, string Name, string Description,
    string Author, string License, string Category, string Language, string BuildSystem, ImmutableArray<TemplateParameter> Parameters,
    ImmutableArray<TemplateFile> Files, string? Icon = null, ImmutableArray<string> Tags = default,
    ImmutableArray<string> RequiresCapabilities = default, int? MinimumStandardOrder = null);

public sealed record ProjectTemplate(string Directory, TemplateManifest Manifest);
public sealed record TemplateProblem(string Path, string Message);

public sealed record TemplateCatalog(ImmutableArray<ProjectTemplate> Templates, ImmutableArray<TemplateProblem> Problems)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<TemplateParameterKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<TemplateValidation>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static TemplateCatalog Discover(IEnumerable<string> roots, CancellationToken cancel)
    {
        Dictionary<string, ProjectTemplate> found = new(StringComparer.Ordinal);
        List<TemplateProblem> problems = [];
        foreach (string root in roots)
        {
            cancel.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                continue;
            }
            try
            {
                TemplatePaths.CheckAncestors(root);
                EnumerationOptions search = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (string manifest in Directory.EnumerateFiles(root, "template.json", search).Order(StringComparer.Ordinal))
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        ProjectTemplate template = Read(manifest);
                        if (!found.TryAdd(template.Manifest.Id, template))
                        {
                            problems.Add(new(manifest, $"Template ID '{template.Manifest.Id}' is already registered."));
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                    {
                        problems.Add(new(manifest, error.Message));
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                problems.Add(new(root, error.Message));
            }
        }
        return new([.. found.Values.OrderBy(template => template.Manifest.Id, StringComparer.Ordinal)], [.. problems]);
    }

    private static ProjectTemplate Read(string path)
    {
        using FileStream source = File.OpenRead(path);
        TemplateManifest manifest = JsonSerializer.Deserialize<TemplateManifest>(source, Json) ?? throw new JsonException("Missing template manifest.");
        if (manifest.SchemaVersion != 1 || !Version.TryParse(manifest.Version, out _) || string.IsNullOrWhiteSpace(manifest.Id)
            || string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Description) || string.IsNullOrWhiteSpace(manifest.Author)
            || string.IsNullOrWhiteSpace(manifest.License) || string.IsNullOrWhiteSpace(manifest.Category) || string.IsNullOrWhiteSpace(manifest.Language)
            || string.IsNullOrWhiteSpace(manifest.BuildSystem) || manifest.Parameters.IsDefault || manifest.Files.IsDefaultOrEmpty)
        {
            throw new JsonException("Expected a version 1 manifest with template metadata, parameters and files.");
        }
        HashSet<string> parameters = new(StringComparer.Ordinal);
        foreach (TemplateParameter parameter in manifest.Parameters)
        {
            if (parameter is null || !TemplatePaths.Identifier(parameter.Id) || string.IsNullOrWhiteSpace(parameter.Label)
                || parameter.When is { } rule && !parameters.Contains(rule.Parameter) || !parameters.Add(parameter.Id))
            {
                throw new JsonException("Parameter IDs must be unique identifiers with a label. A condition must refer to an earlier parameter.");
            }
        }
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach (TemplateFile file in manifest.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Destination) || file.When is { } rule && !parameters.Contains(rule.Parameter))
            {
                throw new JsonException("A template file needs a source and destination.");
            }
            string resource = TemplatePaths.RelativeTo(directory, file.Source);
            TemplatePaths.CheckAncestors(resource);
            if (!File.Exists(resource))
            {
                throw new IOException($"Template resource '{file.Source}' is missing.");
            }
        }
        return new(directory, manifest);
    }
}

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pitstop.Templates;

public sealed record TemplateEnvironment(LanguageStandards Standards, IReadOnlySet<string> VerifiedStandards,
    IReadOnlySet<string> Capabilities, IReadOnlySet<string> Toolchains);

public sealed record PlannedFile(string RelativePath, ImmutableArray<byte> Contents, bool Binary = false);

public sealed record GenerationPlan(string Destination, ImmutableArray<PlannedFile> Files)
{
    public async Task WriteAsync(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        string destination = Path.GetFullPath(Destination);
        string parent = TemplateGeneration.CheckDestination(destination);
        // Put the stage under the destination's parent so the final directory move stays on one volume.
        string stage = Path.Combine(parent, $".pitstop-template-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        bool installed = false;
        try
        {
            foreach (PlannedFile file in Files)
            {
                cancel.ThrowIfCancellationRequested();
                string path = TemplatePaths.RelativeTo(stage, file.RelativePath);
                TemplatePaths.CheckAncestors(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await output.WriteAsync(file.Contents.AsMemory(), cancel).ConfigureAwait(false);
            }
            cancel.ThrowIfCancellationRequested();
            TemplateGeneration.CheckDestination(destination);
            Directory.Move(stage, destination);
            installed = true;
        }
        finally
        {
            if (!installed && Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }
    }
}

public static class TemplateGeneration
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<GenerationPlan> PlanAsync(ProjectTemplate template, string destination, TemplateEnvironment environment,
        IReadOnlyDictionary<string, string> supplied, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        string absolute = Path.GetFullPath(destination);
        CheckDestination(absolute);
        TemplateManifest manifest = template.Manifest;
        if (!manifest.RequiresCapabilities.IsDefaultOrEmpty && manifest.RequiresCapabilities.Any(capability => !environment.Capabilities.Contains(capability)))
        {
            throw new IOException("The selected toolchain lacks a capability required by this template.");
        }
        Dictionary<string, string> values = Bind(manifest, environment, supplied);
        List<PlannedFile> files = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> folders = new(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateFile file in manifest.Files)
        {
            cancel.ThrowIfCancellationRequested();
            if (!Matches(file.When, values))
            {
                continue;
            }
            string relative = TemplateRenderer.Render(file.Destination, values, filename: true).Replace('\\', '/');
            string target = TemplatePaths.RelativeTo(absolute, relative);
            if (!paths.Add(target) || folders.Contains(target))
            {
                throw new IOException($"Multiple outputs use '{relative}'.");
            }
            for (string? parent = Path.GetDirectoryName(target); parent is not null && parent != absolute; parent = Path.GetDirectoryName(parent))
            {
                if (paths.Contains(parent))
                {
                    throw new IOException($"A file conflicts with a directory in '{relative}'.");
                }
                folders.Add(parent);
            }
            string input = TemplatePaths.RelativeTo(template.Directory, file.Source);
            TemplatePaths.CheckAncestors(input);
            byte[] bytes = await File.ReadAllBytesAsync(input, cancel).ConfigureAwait(false);
            if (!file.Binary)
            {
                string content = Utf8.GetString(bytes.AsSpan().StartsWith(Utf8Bom) ? bytes.AsSpan(3) : bytes);
                bytes = Utf8.GetBytes(TemplateRenderer.Render(content, values, filename: false));
            }
            files.Add(new(relative, [.. bytes], file.Binary));
        }
        return new(absolute, [.. files]);
    }

    private static ReadOnlySpan<byte> Utf8Bom => [0xef, 0xbb, 0xbf];

    internal static string CheckDestination(string destination)
    {
        TemplatePaths.CheckAncestors(destination);
        if (!TemplatePaths.FileName(Path.GetFileName(destination)) || Path.GetDirectoryName(destination) is not { } parent || !Directory.Exists(parent))
        {
            throw new IOException("Choose a valid project folder name under an existing directory.");
        }
        if (TemplatePaths.Attributes(destination) is not null)
        {
            throw new IOException($"Destination '{destination}' already exists. Choose a new project folder.");
        }
        return parent;
    }

    private static bool Matches(TemplateCondition? condition, IReadOnlyDictionary<string, string> values) =>
        condition is null || values.GetValueOrDefault(condition.Parameter) == condition.EqualsValue;

    private static Dictionary<string, string> Bind(TemplateManifest manifest, TemplateEnvironment environment, IReadOnlyDictionary<string, string> supplied)
    {
        if (supplied.Keys.Any(key => !manifest.Parameters.Any(parameter => parameter.Id == key)))
        {
            throw new IOException("The template does not declare one or more supplied parameters.");
        }
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        LanguageStandard? standard = null;
        foreach (TemplateParameter parameter in manifest.Parameters)
        {
            if (!Matches(parameter.When, values) || parameter.RequiresCapability is { } capability && !environment.Capabilities.Contains(capability)
                || parameter.RequiresStandardCapability is { } feature
                    && (standard is null || standard.Capabilities.IsDefaultOrEmpty || !standard.Capabilities.Contains(feature, StringComparer.Ordinal)))
            {
                if (parameter.Type == TemplateParameterKind.Boolean)
                {
                    values.Add(parameter.Id, "false");
                }
                continue;
            }
            string? given = supplied.GetValueOrDefault(parameter.Id);
            string? value = given ?? parameter.Default.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => parameter.Default.GetString(),
                _ => parameter.Default.GetRawText(),
            };
            if (parameter.Type == TemplateParameterKind.LanguageStandard && given is null)
            {
                LanguageStandard[] available = [.. environment.Standards.Available(manifest, environment.VerifiedStandards)];
                if (!available.Any(item => item.Id == value))
                {
                    value = available.FirstOrDefault(item => item.Recommended)?.Id ?? available.LastOrDefault()?.Id;
                }
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                if (parameter.Required)
                {
                    throw new IOException($"Enter a value for {parameter.Label}.");
                }
                continue;
            }
            bool accepted = parameter.Type switch
            {
                TemplateParameterKind.Boolean => value is "true" or "false",
                TemplateParameterKind.Choice => !parameter.Choices.IsDefault && parameter.Choices.Contains(value, StringComparer.Ordinal),
                TemplateParameterKind.LanguageStandard => environment.Standards.Available(manifest, environment.VerifiedStandards).Any(item => item.Id == value),
                TemplateParameterKind.Toolchain => environment.Toolchains.Contains(value),
                TemplateParameterKind.Directory => Path.IsPathFullyQualified(value),
                TemplateParameterKind.Integer => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                _ => true,
            };
            bool validName = parameter.Validation switch
            {
                TemplateValidation.FileName => TemplatePaths.FileName(value),
                TemplateValidation.TargetName => value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'),
                TemplateValidation.Identifier => TemplatePaths.Identifier(value),
                TemplateValidation.ModuleName => value.Split('.').All(TemplatePaths.Identifier),
                _ => true,
            };
            if (!accepted || !validName)
            {
                throw new IOException($"'{value}' is invalid or unavailable for {parameter.Label}.");
            }
            values.Add(parameter.Id, value);
            if (parameter.Type == TemplateParameterKind.LanguageStandard)
            {
                standard = environment.Standards.Entries.First(item => item.Id == value);
            }
        }
        return values;
    }
}

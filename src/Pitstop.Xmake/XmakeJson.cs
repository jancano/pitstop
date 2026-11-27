using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pitstop.Core;

namespace Pitstop.Xmake;

internal static class XmakeJson
{
    private static readonly JsonSerializerOptions Options = new() { RespectNullableAnnotations = true };

    internal static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException("Xmake returned a null JSON value.");

    internal static T ReadCommand<T>(string output)
    {
        try
        {
            return Read<T>(output);
        }
        catch (JsonException original)
        {
            // Project scripts may print messages around Xmake's JSON. Its top-level value starts on its own line.
            for (int at = output.Length - 1; at >= 0; at--)
            {
                if (output[at] is not ('{' or '[') || at != 0 && output[at - 1] != '\n')
                {
                    continue;
                }
                try
                {
                    Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(output[at..]));
                    using JsonDocument document = JsonDocument.ParseValue(ref reader);
                    return document.RootElement.Deserialize<T>(Options) ?? throw new JsonException();
                }
                catch (JsonException)
                {
                    // Look back to the preceding candidate.
                }
            }
            throw new JsonException("Xmake did not return the expected JSON output.", original);
        }
    }

    internal static XmakeConfiguration Configuration(string root, ProjectInfo info, Evaluation evaluation) =>
        new(info.Name ?? Path.GetFileName(root), info.Mode, info.Platform, info.Architecture, FullPath(root, info.BuildDirectory),
            FullPath(root, evaluation.ConfigurationFile), evaluation.Toolchain);

    // Xmake 3.1.2 encodes empty Lua tables as {} in both the target list and the graph's targets.
    internal static T[] Array<T>(JsonElement value) => value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any()
        ? [] : value.Deserialize<T[]>(Options) ?? throw new JsonException("Xmake returned no list.");

    internal static XmakeTargetGraph Graph(GraphInfo info) => new([.. info.Roots],
        Array<GraphTarget>(info.Targets).ToImmutableDictionary(target => target.Name, target => ImmutableArray.CreateRange(target.Dependencies), StringComparer.Ordinal));

    internal static XmakeTarget Target(string root, string name, TargetInfo info, EvaluatedTarget evaluated) =>
        new(name, info.Kind, FullPath(root, info.Script), info.Output is { } output ? FullPath(root, output) : null,
            FullPaths(root, evaluated.Sources), FullPaths(root, evaluated.Headers),
            [.. (info.Packages ?? []).Select(p => new XmakePackage(p.Name))],
            new([.. evaluated.Toolchains], [.. (info.Compilers ?? []).Select(c => new XmakeCompiler(c.SourceKind, c.Program, c.Flags, c.TargetFlags))],
                info.Linker is { } linker ? new XmakeLinker(linker.Kind, linker.Program, linker.Flags, linker.TargetFlags) : null));

    internal static ImmutableArray<string> FullPaths(string root, IEnumerable<string> paths) =>
        [.. paths.Select(path => FullPath(root, path)).Distinct(PathNames.Comparer)];

    private static string FullPath(string root, string path) => PathNames.Canonical(Path.GetFullPath(path, root));

    internal sealed record ShowInfo([property: JsonPropertyName("project"), JsonRequired] ProjectInfo Project);

    internal sealed record ProjectInfo(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("mode"), JsonRequired] string Mode,
        [property: JsonPropertyName("plat"), JsonRequired] string Platform,
        [property: JsonPropertyName("arch"), JsonRequired] string Architecture,
        [property: JsonPropertyName("builddir"), JsonRequired] string BuildDirectory,
        [property: JsonPropertyName("projectfile"), JsonRequired] string ProjectFile);

    internal sealed record GraphInfo(
        [property: JsonPropertyName("root_targets"), JsonRequired] string[] Roots,
        [property: JsonPropertyName("targets"), JsonRequired] JsonElement Targets);

    internal sealed record GraphTarget(
        [property: JsonPropertyName("name"), JsonRequired] string Name,
        [property: JsonPropertyName("deps"), JsonRequired] string[] Dependencies);

    internal sealed record TargetInfo(
        [property: JsonPropertyName("kind"), JsonRequired] string Kind,
        [property: JsonPropertyName("at"), JsonRequired] string Script,
        [property: JsonPropertyName("targetfile")] string? Output,
        [property: JsonPropertyName("packages")] PackageInfo[]? Packages,
        [property: JsonPropertyName("compilers")] CompilerInfo[]? Compilers,
        [property: JsonPropertyName("linker")] LinkerInfo? Linker);

    internal sealed record PackageInfo([property: JsonPropertyName("name")] string Name);

    internal sealed record CompilerInfo(
        [property: JsonPropertyName("sourcekind")] string SourceKind,
        [property: JsonPropertyName("program")] string Program,
        [property: JsonPropertyName("flags")] string? Flags,
        [property: JsonPropertyName("flags_with_target")] string? TargetFlags);

    internal sealed record LinkerInfo(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("program")] string Program,
        [property: JsonPropertyName("flags")] string? Flags,
        [property: JsonPropertyName("flags_with_target")] string? TargetFlags);

    // These fields are emitted by Pitstop's adapter from Xmake's evaluated project API.
    internal sealed record Evaluation(
        [property: JsonPropertyName("configuration_file")] string ConfigurationFile,
        [property: JsonPropertyName("toolchain")] string? Toolchain,
        [property: JsonPropertyName("project_files")] string[] ProjectFiles,
        [property: JsonPropertyName("targets")] EvaluatedTarget[] Targets);

    internal sealed record EvaluatedTarget(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("sources")] string[] Sources,
        [property: JsonPropertyName("headers")] string[] Headers,
        [property: JsonPropertyName("toolchains")] string[] Toolchains);
}

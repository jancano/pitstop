using System.Collections.Immutable;
using System.Text;
using Passenger.Core;
using Pitstop.Templates;
using Xunit;

namespace Passenger.UnitTests.Workspace;

public sealed class ProjectTemplateTests : IDisposable
{
    private readonly string root = PathNames.Canonical(Directory.CreateTempSubdirectory("pitstop-template-tests-").FullName);
    private static CancellationToken Stop => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Discovers_local_templates_and_writes_exactly_the_preview()
    {
        ProjectTemplate template = CreateTemplate();
        string invalid = Directory.CreateDirectory(Path.Combine(root, "broken")).FullName;
        File.WriteAllText(Path.Combine(invalid, "template.json"), "{}");
        TemplateCatalog catalog = TemplateCatalog.Discover([root, root], Stop);
        Assert.Equal(template.Manifest.Id, Assert.Single(catalog.Templates).Manifest.Id);
        Assert.Contains(catalog.Problems, problem => problem.Message.Contains("already registered", StringComparison.Ordinal));
        Assert.Contains(catalog.Problems, problem => problem.Path.StartsWith(invalid, StringComparison.Ordinal));

        string target = Path.Combine(root, "new project");
        Dictionary<string, string> values = new() { ["projectName"] = "new project", ["label"] = "A \"quote\"\\path", ["stdModule"] = "true" };
        GenerationPlan plan = await TemplateGeneration.PlanAsync(template, target, Environment(), values, Stop);
        Assert.Equal(new[] { "new project/main.cpp", "blob.bin" }, plan.Files.Select(file => file.RelativePath));
        string preview = Encoding.UTF8.GetString(plan.Files[0].Contents.AsSpan());
        Assert.Equal("// c++29\nconst char* label = \"A \\\"quote\\\"\\\\path\";\n", preview);
        Assert.False(Directory.Exists(target));
        await plan.WriteAsync(Stop);
        foreach (PlannedFile planned in plan.Files)
        {
            Assert.Equal(planned.Contents.ToArray(), await File.ReadAllBytesAsync(Path.Combine(target, planned.RelativePath), Stop));
        }
        await Assert.ThrowsAsync<IOException>(() => plan.WriteAsync(Stop));
    }

    [Fact]
    public async Task Rejects_escapes_collisions_and_unsupported_explicit_standards()
    {
        ProjectTemplate template = CreateTemplate();
        string destination = Path.Combine(root, "new");
        Dictionary<string, string> values = new() { ["projectName"] = "..", ["label"] = "Project" };
        await Assert.ThrowsAsync<IOException>(() => TemplateGeneration.PlanAsync(template, destination, Environment(), values, Stop));
        values["projectName"] = "valid";
        values["standard"] = "c++23";
        await Assert.ThrowsAsync<IOException>(() => TemplateGeneration.PlanAsync(template, destination, Environment(), values, Stop));
        values.Remove("standard");
        TemplateFile first = template.Manifest.Files[0];
        ProjectTemplate collision = template with { Manifest = template.Manifest with { Files = [first, first with { Destination = "VALID/MAIN.CPP" }] } };
        await Assert.ThrowsAsync<IOException>(() => TemplateGeneration.PlanAsync(collision, destination, Environment(), values, Stop));
        GenerationPlan plan = await TemplateGeneration.PlanAsync(template, destination, Environment(), values, Stop);
        using CancellationTokenSource stopped = new();
        stopped.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plan.WriteAsync(stopped.Token));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Rejects_a_destination_under_a_directory_symbolic_link()
    {
        ProjectTemplate template = CreateTemplate();
        string elsewhere = Directory.CreateDirectory(Path.Combine(root, "elsewhere")).FullName;
        string link = Path.Combine(root, "link");
        try
        {
            Directory.CreateSymbolicLink(link, elsewhere);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"This test could not create a directory symbolic link: {error.Message}");
        }
        await Assert.ThrowsAsync<IOException>(() => TemplateGeneration.PlanAsync(template, Path.Combine(link, "new"), Environment(),
            new Dictionary<string, string>(), Stop));
        Assert.False(Directory.Exists(Path.Combine(elsewhere, "new")));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private ProjectTemplate CreateTemplate()
    {
        string directory = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
        File.WriteAllText(Path.Combine(directory, "template.json"), """
            {
              "schemaVersion": 1, "id": "fixture.console", "version": "1.0.0", "name": "Console", "description": "Test project",
              "author": "Pitstop", "license": "MIT", "category": "Applications", "language": "cpp", "buildSystem": "fixture",
              "parameters": [
                { "id": "projectName", "label": "Project", "type": "string", "required": true, "validation": "fileName" },
                { "id": "standard", "label": "Standard", "type": "languageStandard", "default": "c++23", "required": true },
                { "id": "label", "label": "Label", "type": "string", "required": true },
                { "id": "stdModule", "label": "Standard library module", "type": "boolean", "default": true, "requiresCapability": "cpp.std-module" }
              ],
              "files": [
                { "source": "main.txt", "destination": "{{projectName}}/main.cpp" },
                { "source": "blob.bin", "destination": "blob.bin", "binary": true },
                { "source": "module.txt", "destination": "std.cpp", "when": { "parameter": "stdModule", "equalsValue": "true" } }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(directory, "main.txt"), "// {{standard}}\nconst char* label = {{c:label}};\n{{#stdModule}}import std;\n{{/stdModule}}");
        File.WriteAllText(Path.Combine(directory, "module.txt"), "import std;\n");
        File.WriteAllBytes(Path.Combine(directory, "blob.bin"), [0, 255, 34]);
        return Assert.Single(TemplateCatalog.Discover([root], Stop).Templates);
    }

    private static TemplateEnvironment Environment()
    {
        using MemoryStream catalog = new("""
            [{"id":"c++23","label":"C++23","language":"cpp","revision":2023,"order":2023},
             {"id":"c++29","label":"C++29","language":"cpp","revision":2029,"order":2029,"recommended":true}]
            """u8.ToArray());
        return new(LanguageStandards.Read(catalog), ImmutableHashSet.Create("c++29"), ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty);
    }
}

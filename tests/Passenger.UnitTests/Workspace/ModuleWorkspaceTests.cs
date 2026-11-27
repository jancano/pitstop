using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Passenger.Core;
using Pitstop.Modules;
using Pitstop.ProjectSystem;
using Pitstop.Xmake;
using Xunit;

namespace Passenger.UnitTests.Workspace;

public sealed class ModuleWorkspaceTests
{
    [Fact]
    public void Measures_graph_construction_from_a_scale_capture()
    {
        string? directory = Environment.GetEnvironmentVariable("PITSTOP_TEST_MODULE_SCALE_RESULTS");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(directory), "Set PITSTOP_TEST_MODULE_SCALE_RESULTS to a module-scale.py results directory with a successful P1689 scan.");
        string results = Path.GetFullPath(directory!);
        using JsonDocument database = JsonDocument.Parse(File.ReadAllText(Path.Combine(results, "scan-commands.json")));
        string root = PathNames.Canonical(database.RootElement[0].GetProperty("directory").GetString()!);
        string script = Path.Combine(root, "xmake.lua");
        XmakeTargetGraph targets = XmakeJson.Graph(XmakeJson.ReadCommand<XmakeJson.GraphInfo>(File.ReadAllText(Path.Combine(results, "target-graph.out"))));
        XmakeWorkspace workspace = new(root, new("xmake", new(3, 1, 2), "3.1.2"),
            new("scale", "debug", "mingw", "x86_64", Path.Combine(root, "build"), script, "llvm"),
            [.. targets.Edges.Keys.Select(name => new XmakeTarget(name, "", script, null, [], [], [], new([], [], null)))],
            targets, [], [script], null, null);
        (List<ScanCommand> commands, List<string> omitted) = ModuleScanner.Commands(workspace, database.RootElement, CancellationToken.None);
        ImmutableArray<P1689.Rule> rules = P1689.Read(File.ReadAllText(Path.Combine(results, "p1689.out")));
        Assert.NotEmpty(commands);
        long started = Stopwatch.GetTimestamp();
        ModuleWorkspace graph = P1689.Resolve(workspace, commands, rules, omitted, null, CancellationToken.None);
        double graphSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        Assert.NotEmpty(graph.Providers);
        File.WriteAllText(Path.Combine(results, "graph-measurements.json"), JsonSerializer.Serialize(new
        {
            graphSeconds, providers = graph.Providers.Length, resolved = graph.Dependencies.Count(edge => edge.Provider is not null),
        }) + "\n");
    }

    [Fact]
    public void Reads_the_captured_compiler_and_Xmake_scans_and_distinguishes_partitions()
    {
        string repo = FoldersUp.From(AppContext.BaseDirectory).First(path => File.Exists(Path.Combine(path, "Passenger.slnx")));
        string samples = Path.Combine(repo, "docs", "architecture", "cpp-toolchain-samples");
        ImmutableArray<P1689.Rule> capture = P1689.Read(File.ReadAllText(Path.Combine(samples, "p1689.json")));
        Assert.Contains(capture.SelectMany(rule => rule.Provides.IsDefault ? [] : rule.Provides), entry => entry is { Name: "arithmetic:detail", Interface: false });
        Assert.Contains(capture.SelectMany(rule => rule.Provides.IsDefault ? [] : rule.Provides), entry => entry is { Name: "arithmetic:numbers", Interface: true });
        Assert.Null(Assert.Single(Assert.Single(P1689.Read(File.ReadAllText(Path.Combine(samples, "xmake-native-module-scan.json")))).Requires).Source);
    }

    [Fact]
    public void Resolves_provider_identity_in_target_context_and_detects_only_translation_unit_cycles()
    {
        string root = AppContext.BaseDirectory;
        XmakeWorkspace workspace = new(root, new("xmake", new Version(3, 1, 2), "3.1.2"),
            new("test", "release", "mingw", "x86_64", root, "xmake.conf", "llvm"), [],
            new([], ImmutableDictionary<string, ImmutableArray<string>>.Empty.Add("consumer", ["lib", "second"])), [], [], null, null);
        ScanCommand[] commands = [Command(0, "lib", "provider"), Command(1, "lib", "part"), Command(2, "second", "duplicate"),
            Command(3, "consumer", "main"), Command(4, "consumer", "partial")];
        ImmutableArray<P1689.Rule> rules = P1689.Read("""
            {"version":1,"rules":[
              {"primary-output":"provider.obj","provides":[{"logical-name":"tools","is-interface":true}],"requires":[{"logical-name":"tools:private"}]},
              {"primary-output":"part.obj","provides":[{"logical-name":"tools:private","is-interface":false}],"requires":[{"logical-name":"tools"}]},
              {"primary-output":"duplicate.obj","provides":[{"logical-name":"tools"}]},
              {"primary-output":"main.obj","requires":[{"logical-name":"tools"},{"logical-name":"tools","source-path":"provider.cpp"},{"logical-name":"missing"}]}
            ]}
            """);
        ModuleWorkspace graph = P1689.Resolve(workspace, commands, rules, [], null, CancellationToken.None);
        Assert.False(graph.IsComplete);
        Assert.EndsWith("partial.cpp", Assert.Single(graph.Missing), StringComparison.Ordinal);
        Assert.Equal(ModuleUnitKind.InternalPartition, Assert.Single(graph.Named("tools:private")).Kind);
        Assert.Equal(2, graph.Named("tools").Count());
        Assert.Equal(new[] { ModuleResolution.Ambiguous, ModuleResolution.Resolved, ModuleResolution.Unresolved }, graph.Imports(commands[3].Unit).Select(edge => edge.Resolution));
        Assert.True(graph.IsCyclic(commands[0].Unit));
        Assert.Equal(new[] { 1, 3 }, graph.Reachable(commands[0].Unit, consumers: true).Select(unit => unit.Id));
        Assert.DoesNotContain(graph.Providers, provider => provider.Unit.Id == 3);

        commands[3] = commands[3] with { BuildModules = ImmutableDictionary<string, string>.Empty.Add("tools", commands[0].Unit.Output) };
        ModuleWorkspace bound = P1689.Resolve(workspace, commands, P1689.Read("""
            {"version":1,"rules":[
              {"primary-output":"provider.obj","provides":[{"logical-name":"tools","is-interface":true}]},
              {"primary-output":"duplicate.obj","provides":[{"logical-name":"tools","is-interface":true}]},
              {"primary-output":"main.obj","requires":[{"logical-name":"tools","source-path":"duplicate.cpp"}]}
            ]}
            """), [], null, CancellationToken.None);
        ModuleDependency actual = Assert.Single(bound.Imports(commands[3].Unit));
        Assert.Equal(commands[0].Unit, actual.Provider?.Unit);
        Assert.Equal(ModuleResolutionEvidence.XmakeCommand, actual.Evidence);
        Assert.EndsWith("duplicate.cpp", actual.ReportedSource, StringComparison.Ordinal);

        ScanCommand Command(int id, string target, string name) => new(new(id, Path.Combine(root, $"{name}.cpp"),
            Path.Combine(root, $"{name}.obj"), new(target, "clang++", "c++23", target)), root, $"{name}.cpp", [], $"{name}.obj");
    }

    [Fact]
    public void Keeps_a_shared_translation_unit_in_each_evaluated_target_and_honors_cancellation()
    {
        string repo = FoldersUp.From(AppContext.BaseDirectory).First(path => File.Exists(Path.Combine(path, "Passenger.slnx")));
        string root = PathNames.Canonical(Path.Combine(repo, "tests", "CppWorkspaces", "module-contexts"));
        string source = PathNames.Canonical(Path.Combine(root, "selection.cpp"));
        string script = Path.Combine(root, "xmake.lua");
        XmakeTarget first = new("first", "binary", script, null, [source], [], [], new([], [], null));
        XmakeWorkspace workspace = new(root, new("xmake", new(3, 1, 2), "3.1.2"),
            new("test", "debug", "mingw", "x86_64", Path.Combine(root, "build"), script, "llvm"),
            [first, first with { Name = "second" }], new([], ImmutableDictionary<string, ImmutableArray<string>>.Empty), [], [script], null, null);
        JsonElement commands = JsonSerializer.SerializeToElement(new[]
        {
            new { directory = root, file = source, arguments = new[] { "clang++", "-std=c++23", "-DFIRST", "-c", source, "-o", "build/.objs/first/shared.o" } },
            new { directory = root, file = source, arguments = new[] { "clang++", "-std=c++23", "-DSECOND", "-c", source, "-o", "build/.objs/second/shared.o" } },
        });
        (List<ScanCommand> scan, List<string> omitted) = ModuleScanner.Commands(workspace, commands, CancellationToken.None);
        Assert.Empty(omitted);
        Assert.Collection(scan, command => Assert.Equal("first", command.Unit.Context.Target), command => Assert.Equal("second", command.Unit.Context.Target));
        Assert.All(scan, command => Assert.Equal(ModuleTargetEvidence.XmakeOutputPath, command.Unit.Context.TargetEvidence));
        Assert.NotEqual(scan[0].Unit.Context.CommandKey, scan[1].Unit.Context.CommandKey);
        Assert.Throws<OperationCanceledException>(() => ModuleScanner.Commands(workspace, commands, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Uses_the_providers_output_ownership_without_requiring_Xmake_paths()
    {
        string repo = FoldersUp.From(AppContext.BaseDirectory).First(path => File.Exists(Path.Combine(path, "Passenger.slnx")));
        string root = PathNames.Canonical(Path.Combine(repo, "tests", "CppWorkspaces", "module-contexts"));
        string source = PathNames.Canonical(Path.Combine(root, "selection.cpp"));
        FixtureWorkspace workspace = new(root, [new FixtureTarget("first", [source]), new FixtureTarget("second", [source])]);
        JsonElement commands = JsonSerializer.SerializeToElement(new[]
        {
            new { directory = root, file = source, arguments = new[] { "clang++", "-std=c++23", "-c", source, "-o", "artifacts/selection.o" } },
        });
        (List<ScanCommand> scan, List<string> omitted) = ModuleScanner.Commands(workspace, commands, TestContext.Current.CancellationToken);
        Assert.Empty(omitted);
        ModuleContext context = Assert.Single(scan).Unit.Context;
        Assert.Equal("second", context.Target);
        Assert.StartsWith("fixture|", context.ConfigurationKey, StringComparison.Ordinal);
        Assert.True(ModuleWorkspace.VisibleTargets(workspace, "second").SetEquals(["first", "second"]));
    }

    private sealed record FixtureTarget(string Name, ImmutableArray<string> SourceFiles) : INativeTarget
    {
        public string Kind => "static";
        public string? Output => null;
        public ImmutableArray<string> HeaderFiles => [];
        public IEnumerable<NativeLanguage> CompilerLanguages => [NativeLanguage.Cpp];
    }

    private sealed record FixtureWorkspace(string Root, IReadOnlyList<INativeTarget> Targets) : INativeWorkspace
    {
        public string ProviderId => "fixture";
        public string ProviderName => "Fixture";
        public NativeConfiguration Configuration => new("debug", "fixture", "x64", "llvm");
        public NativeCompilationDatabases? CompilationDatabases => null;
        public string? CompilationDatabaseProblem => null;
        public ImmutableArray<string> DependenciesOf(string target) => target == "second" ? ["first"] : [];
        public string? MatchOutputTarget(string? output, IReadOnlyCollection<string> candidates) =>
            output == "artifacts/selection.o" && candidates.Contains("second", StringComparer.Ordinal) ? "second" : null;
    }

    [Fact]
    public void Generates_module_units_and_rejects_invalid_names()
    {
        Assert.Equal("export module example.math;\n\nimport std;\n", ModuleCreation.Source("example.math", "", ModuleUnitKind.PrimaryInterface, true));
        Assert.Equal("export module example.math:numbers;\n", ModuleCreation.Source("example.math", "numbers", ModuleUnitKind.InterfacePartition, false));
        Assert.Equal("module example.math:detail;\n", ModuleCreation.Source("example.math", "detail", ModuleUnitKind.InternalPartition, false));
        Assert.Equal("module example.math;\n", ModuleCreation.Source("example.math", "", ModuleUnitKind.Implementation, false));
        Assert.Throws<ArgumentException>(() => ModuleCreation.Source("std", "", ModuleUnitKind.PrimaryInterface, false));
        Assert.Throws<ArgumentException>(() => ModuleCreation.Source("some.class", "", ModuleUnitKind.PrimaryInterface, false));
    }
}

using System.Text.Json;
using Passenger.Core;
using Pitstop.Xmake;
using Xunit;

namespace Passenger.UnitTests.Workspace;

public sealed class XmakeMetadataTests
{
    [Theory]
    [InlineData("3.1.2+HEAD.5d21e9daf", true)]
    [InlineData("3.2.0", true)]
    [InlineData("3.1.1", false)]
    [InlineData("3.1.2-rc1", false)]
    [InlineData("4.0.0", false)]
    [InlineData("", false)]
    public void Validates_the_Xmake_JSON_API_version(string version, bool supported)
    {
        string banner = $"xmake v{version}, A cross-platform build utility based on Lua";
        if (supported)
        {
            Assert.Equal(version, XmakeService.Installation("xmake", banner).BuildVersion);
        }
        else
        {
            Assert.Contains("3.1.2", Assert.Throws<XmakeException>(() => XmakeService.Installation("xmake", banner)).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Reads_target_outputs_graph_and_configuration_from_the_Phase_1_captures()
    {
        string root = AppContext.BaseDirectory;
        XmakeJson.TargetInfo detail = XmakeJson.Read<XmakeJson.TargetInfo>(Sample("target-arithmetic.json"));
        XmakeJson.ProjectInfo settings = XmakeJson.Read<XmakeJson.ShowInfo>(Sample("configuration.json")).Project;
        XmakeTargetGraph graph = XmakeJson.Graph(XmakeJson.Read<XmakeJson.GraphInfo>(Sample("target-graph.json")));
        XmakeJson.Evaluation evaluated = new(".xmake/windows/x64/xmake.conf", "llvm", ["xmake.lua"],
            [new("arithmetic", ["arithmetic.cppm", "detail.cppm", "implementation.cpp", "numbers.cppm"], [], ["llvm"]),
             new("modules", ["main.cpp", "implementation.cpp"], [], ["llvm"])]);
        XmakeTarget library = XmakeJson.Target(root, "arithmetic", detail, evaluated.Targets[0]);
        XmakeTarget consumer = XmakeJson.Target(root, "modules", detail with { Kind = "binary", Output = "build/modules.exe" }, evaluated.Targets[1]);
        XmakeConfiguration active = XmakeJson.Configuration(root, settings, evaluated);

        Assert.Equal("static", library.Kind);
        Assert.EndsWith("libarithmetic.a", library.Output, StringComparison.Ordinal);
        Assert.EndsWith("modules.exe", consumer.Output, StringComparison.Ordinal);
        Assert.Equal("cxx", Assert.Single(library.Toolchain.Compilers).SourceKind);
        Assert.Equal("ar", library.Toolchain.Linker?.Kind);
        Assert.Equal("llvm", Assert.Single(library.Toolchain.Names));
        Assert.Equal(("debug", "mingw", "x86_64", "llvm"), (active.Mode, active.Platform, active.Architecture, active.Toolchain));
        Assert.Equal("modules", Assert.Single(graph.Roots));
        Assert.Equal("arithmetic", Assert.Single(graph.DependenciesOf("modules")));
        Assert.Empty(graph.DependenciesOf("arithmetic"));
        Assert.Equal(PathNames.Canonical(Path.Combine(root, "implementation.cpp")), Assert.Single(library.SourceFiles.Intersect(consumer.SourceFiles, PathNames.Comparer)));
    }

    [Fact]
    public void Finds_Xmake_JSON_alongside_project_messages()
    {
        string output = "Starting configuration\n[\"arithmetic\",\"modules\"]\nwarning: local options changed\n";
        Assert.Equal(new[] { "arithmetic", "modules" }, XmakeJson.ReadCommand<string[]>(output));
    }

    [Fact]
    public void Reads_the_empty_table_encoding_in_Xmake_target_lists()
    {
        Assert.Empty(XmakeJson.Array<string>(XmakeJson.ReadCommand<JsonElement>("{}")));
        XmakeTargetGraph graph = XmakeJson.Graph(XmakeJson.Read<XmakeJson.GraphInfo>("""{"root_targets":[],"targets":{}}"""));
        Assert.Empty(graph.Roots);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Derives_a_separate_semantic_database_without_losing_translation_unit_contexts()
    {
        string original = Sample("compile-commands-consumer.json");
        using JsonDocument raw = JsonDocument.Parse(original);
        using JsonDocument semantic = JsonDocument.Parse(XmakeService.SemanticDatabase(original));
        JsonElement before = raw.RootElement[0];
        JsonElement after = semantic.RootElement[0];
        Assert.Contains(before.GetProperty("arguments").EnumerateArray(), flag => flag.GetString()!.StartsWith("-fmodule-file=arithmetic=", StringComparison.Ordinal));
        Assert.DoesNotContain(after.GetProperty("arguments").EnumerateArray(), flag => flag.GetString()!.StartsWith("-fmodule-file=", StringComparison.Ordinal));
        Assert.Equal(before.GetProperty("directory").GetString(), after.GetProperty("directory").GetString());
        Assert.Equal(before.GetProperty("file").GetString(), after.GetProperty("file").GetString());
        Assert.EndsWith("main.cpp.obj", after.GetProperty("output").GetString(), StringComparison.Ordinal);

        string duplicates = """
            [
              {"file":"same.cpp","directory":"/one","arguments":["clang++","-imsvc","headers","-o","one.o"]},
              {"file":"same.cpp","directory":"/two","arguments":["clang-cl","-imsvc","headers","/Fo2.obj"]}
            ]
            """;
        using JsonDocument distinct = JsonDocument.Parse(XmakeService.SemanticDatabase(duplicates));
        Assert.Equal(2, distinct.RootElement.GetArrayLength());
        Assert.Equal("-isystem", distinct.RootElement[0].GetProperty("arguments")[1].GetString());
        Assert.Equal("-imsvc", distinct.RootElement[1].GetProperty("arguments")[1].GetString());
    }

    private static string Sample(string name)
    {
        string repository = FoldersUp.From(AppContext.BaseDirectory).First(at => File.Exists(Path.Combine(at, "Passenger.slnx")));
        return File.ReadAllText(Path.Combine(repository, "docs", "architecture", "cpp-toolchain-samples", name));
    }
}

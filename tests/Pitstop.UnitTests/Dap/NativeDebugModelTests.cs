using System.Collections.Immutable;
using System.Text.Json;
using Pitstop.Core;
using Pitstop.Modules;
using Pitstop.NativeDebug;
using Pitstop.ProjectSystem;

namespace Pitstop.UnitTests.Dap;

public sealed class NativeDebugModelTests
{
    [Fact]
    public void An_explicit_path_takes_precedence_and_a_missing_explicit_path_does_not_fall_back()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("pitstop-dap-select-");
        try
        {
            string fromSettings = Path.Combine(directory.FullName, "lldb-dap");
            string explicitly = Path.Combine(directory.FullName, "custom-dap");
            File.WriteAllText(fromSettings, "");
            File.WriteAllText(explicitly, "");
            Assert.Equal(PathNames.Canonical(explicitly),
                LldbDiscovery.Select(explicitly, directory.FullName, "lldb-dap", fromSettings, null));
            Assert.Equal(PathNames.Canonical(fromSettings),
                LldbDiscovery.Select(null, directory.FullName, "lldb-dap", explicitly, null));
            Assert.Equal(NativeDebugFailure.MissingTool,
                Assert.Throws<NativeDebugException>(() => LldbDiscovery.Select("not-absolute", directory.FullName, "lldb-dap", fromSettings, null)).Failure);
            Assert.Equal(NativeDebugFailure.MissingTool,
                Assert.Throws<NativeDebugException>(() => LldbDiscovery.Select(null, null, "lldb-dap", null, null)).Failure);
        }
        finally
        {
            File.Delete(Path.Combine(directory.FullName, "lldb-dap"));
            File.Delete(Path.Combine(directory.FullName, "custom-dap"));
            directory.Delete();
        }
    }

    [Fact]
    public void Version_validation_uses_both_the_adapter_and_its_loaded_runtime()
    {
        (Version adapter, Version runtime) = LldbDiscovery.ReadVersions("""
            lldb-dap:
              LLVM version 23.1.6 (reference)
            liblldb: lldb version 23.1.5-dev
            """);
        Assert.Equal(new Version(23, 1, 6), adapter);
        Assert.Equal(new Version(23, 1, 5), runtime);
        Assert.Equal(NativeDebugFailure.InvalidTool,
            Assert.Throws<NativeDebugException>(() => LldbDiscovery.ReadVersions("lldb version 23.1.6")).Failure);
    }

    [Fact]
    public void Transcripts_record_request_correlation_without_payload_or_custom_names()
    {
        const string secret = "do-not-record";
        NativeDebugPacket request = NativeDebugPacket.From("""
            --> {"type":"request","seq":2,"command":"launch","arguments":{"env":{"TOKEN":"do-not-record"}}}
            """);
        NativeDebugPacket response = NativeDebugPacket.From("""
            <-- {"type":"response","seq":9,"request_seq":2,"success":false,"command":"launch","message":"do-not-record"}
            """);
        NativeDebugPacket unknown = NativeDebugPacket.From("""
            <-- {"type":"event","seq":10,"event":"do-not-record","body":{"file":"do-not-record"}}
            """);
        Assert.Equal(request.Sequence, response.RequestSequence);
        Assert.Equal("(other)", unknown.Name);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(new[] { request, response, unknown }), StringComparison.Ordinal);
    }

    [Fact]
    public void Source_context_keeps_ambiguous_membership_and_rejects_previous_snapshots()
    {
        string root = PathNames.Canonical(Path.GetTempPath());
        string path = Path.Combine(root, "unrelated-name.cc");
        FakeWorkspace active = new(root);
        ModuleContext first = new("a", "clang", "c++23", "first");
        ModuleContext second = new("b", "clang", "c++23", "second");
        TranslationUnit iface = new(1, path, "iface.o", first);
        TranslationUnit unknown = new(2, path, "unknown.o", second);
        ModuleProvider provider = new(new("my.project:part", first), iface, ModuleUnitKind.InterfacePartition, ModuleOrigin.Workspace);
        ModuleWorkspace snapshot = new(active, [iface, unknown], [provider], [], [], null);

        DebugModuleContext context = DebugModuleContext.For(snapshot, active, Path.Combine(root, ".", "unrelated-name.cc"));
        Assert.Equal([iface, unknown], context.Units);
        Assert.Equal([provider], context.Providers);
        Assert.Empty(DebugModuleContext.For(snapshot, new FakeWorkspace(root), path).Units);
        Assert.Empty(DebugModuleContext.For(snapshot, active, "unrelated-name.cc").Units);
    }

    private sealed record FakeWorkspace(string Root) : INativeWorkspace
    {
        public string ProviderId => "fake";
        public string ProviderName => "Fake";
        public NativeConfiguration Configuration => new("debug", "local", "x64", null);
        public IReadOnlyList<INativeTarget> Targets => [];
        public NativeCompilationDatabases? CompilationDatabases => null;
        public string? CompilationDatabaseProblem => null;
        public ImmutableArray<string> DependenciesOf(string target) => [];
        public string? MatchOutputTarget(string? output, IReadOnlyCollection<string> candidates) => null;
    }
}

using System.Collections.Immutable;
using System.Diagnostics;
using Pitstop.Core;
using Pitstop.Xmake;
using Xunit;

namespace Pitstop.UnitTests.Workspace;

public sealed class XmakeOperationsTests
{
    [Fact]
    public void Project_edit_plans_reject_changed_scripts_cycles_and_revoked_trust()
    {
        string root = PathNames.Canonical(Directory.CreateTempSubdirectory("pitstop-xmake-edit-").FullName);
        string script = Path.Combine(root, "xmake.lua");
        File.WriteAllText(script, "set_project(\"proposed\")\r\n");
        using IDisposable ask = WorkspaceTrust.Ask(root);
        try
        {
            XmakeTarget binary = new("app", "binary", script, null, [], [], [], new([], [], null));
            XmakeTarget library = binary with { Name = "library", Kind = "static" };
            XmakeWorkspace workspace = new(root, new("xmake", new(3, 1, 2), "3.1.2"),
                new("proposed", "debug", "mingw", "x86_64", root, script, "llvm"), [binary, library],
                new(["app"], ImmutableDictionary<string, ImmutableArray<string>>.Empty.Add("app", ["library"])), [], [script], null, null);
            Assert.Throws<UntrustedFolderException>(() => XmakeProjectEdits.AddTarget(workspace, "other", "binary"));
            WorkspaceTrust.Trust(root);
            Assert.Throws<ArgumentException>(() => XmakeProjectEdits.AddDependency(workspace, library, binary));
            Assert.Throws<ArgumentException>(() => XmakeProjectEdits.NewSourcePath(workspace, "../escaped.cpp", ".cpp"));
            XmakeScriptEdit edit = XmakeProjectEdits.AddTarget(workspace, "other", "binary");
            Assert.Contains("\r\n    set_kind", edit.Addition, StringComparison.Ordinal);
            File.AppendAllText(script, "-- edited while the preview was open\r\n");
            Assert.Throws<IOException>(() => edit.Apply(edit.Addition));
            XmakeScriptEdit valid = XmakeProjectEdits.AddTarget(workspace, "other", "binary");
            WorkspaceTrust.TakeBack(root);
            Assert.Throws<UntrustedFolderException>(() => valid.Apply(valid.Addition));
            Assert.DoesNotContain("set_kind", File.ReadAllText(script), StringComparison.Ordinal);
        }
        finally
        {
            WorkspaceTrust.TakeBack(root);
            File.Delete(script);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void Uses_the_evaluated_target_and_leaves_configuration_intact_when_cleaning()
    {
        string root = PathNames.Canonical(Directory.CreateTempSubdirectory("pitstop-xmake-command-").FullName);
        string script = Path.Combine(root, "xmake.lua");
        File.WriteAllText(script, string.Empty);
        using IDisposable ask = WorkspaceTrust.Ask(root);
        XmakeTarget binary = new("app", "binary", script, null, [], [], [], new([], [], null));
        XmakeWorkspace snapshot = new(root, new("xmake", new Version(3, 1, 2), "3.1.2"),
            new("probe", "release", "mingw", "x86_64", root, script, "llvm"), [binary, binary with { Name = "library", Kind = "static" }],
            new([], ImmutableDictionary<string, ImmutableArray<string>>.Empty), [], [script], null, null);
        try
        {
            Assert.Throws<UntrustedFolderException>(() => XmakeOperations.Build(snapshot, XmakeBuildKind.Build, []));
            WorkspaceTrust.Trust(root);
            Assert.Equal(new[] { "--confirm=no", "--all" }, XmakeOperations.Build(snapshot, XmakeBuildKind.Build, []).ArgumentList.Skip(3));
            Assert.Equal(new[] { "--confirm=no", "--rebuild", "app" }, XmakeOperations.Build(snapshot, XmakeBuildKind.Rebuild, ["app"]).ArgumentList.Skip(3));
            ProcessStartInfo clean = XmakeOperations.Build(snapshot, XmakeBuildKind.Clean, []);
            Assert.Equal(new[] { "clean", $"--project={root}", $"--file={script}", "--confirm=no" }, clean.ArgumentList);
            Assert.Equal("library", XmakeOperations.Build(snapshot, XmakeBuildKind.Clean, ["library"]).ArgumentList[^1]);
            Assert.Throws<XmakeException>(() => XmakeOperations.Build(snapshot, XmakeBuildKind.Build, ["gone"]));
            Assert.Throws<XmakeException>(() => XmakeOperations.Run(snapshot, "library", [], string.Empty));
            string[] args = ["a b", "a\"b", "café λ", "", "--custom"];
            Assert.Equal(args, XmakeOperations.Run(snapshot, "app", args, ".").ArgumentList.Skip(6));
        }
        finally
        {
            WorkspaceTrust.TakeBack(root);
            File.Delete(script);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void Reads_Clang_locations_and_LLD_details_without_inventing_a_linker_location()
    {
        string root = AppContext.BaseDirectory;
        XmakeBuildOutput parser = new(root);
        foreach (string line in (string[])[
            "[ 23%]: <bad> compiling.debug bad.cppm",
            "error: bad.cppm:1:56: error: member reference base type 'int' is not a structure or union",
            "    1 | export template <typename T> int value(T input) { return input.missing(); }",
            "      |                                                   ~~~~~^~~~~~~~",
            "consumer.cpp:2:21: note: in instantiation of function template specialization 'value<int>' requested here",
            "bad.cppm:3:8: warning: unused variable [-Wunused-variable]",
            "error: ld.lld: error: undefined symbol: missing_definition()",
            ">>> referenced by link-error.cpp:2",
            "clang-23: error: linker command failed with exit code 1 (use -v to see invocation)",
            "error: package dependency could not be resolved",
        ])
        {
            parser.Read(line);
        }
        IReadOnlyList<XmakeDiagnostic> diagnostics = parser.Complete();
        Assert.Equal(6, diagnostics.Count);
        Assert.Equal(("bad.cppm", 1, 56), (Path.GetFileName(diagnostics[0].Path), diagnostics[0].Line, diagnostics[0].Column));
        Assert.Contains("~~~~~^", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Equal((XmakeDiagnosticSeverity.Note, "consumer.cpp", 2), (diagnostics[1].Severity, Path.GetFileName(diagnostics[1].Path), diagnostics[1].Line));
        Assert.Equal(XmakeDiagnosticSeverity.Warning, diagnostics[2].Severity);
        Assert.Contains("referenced by link-error.cpp:2", diagnostics[3].Message, StringComparison.Ordinal);
        Assert.All(diagnostics.Skip(3), diagnostic => Assert.Null(diagnostic.Path));
    }
}

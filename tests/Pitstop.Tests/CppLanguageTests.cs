using System.Collections.Immutable;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Pitstop.App;
using Pitstop.App.Workspace;
using Pitstop.Core;
using Pitstop.Editor;
using Pitstop.Languages;
using Pitstop.Lsp;
using Pitstop.Text;
using Pitstop.Cpp;
using Pitstop.Xmake;

namespace Pitstop.Tests;

public sealed class CppLanguageTests
{
    [Theory]
    [InlineData("file.c", LanguageIds.C)]
    [InlineData("file.h", LanguageIds.C)]
    [InlineData("file.C", LanguageIds.Cpp)]
    [InlineData("file.cpp", LanguageIds.Cpp)]
    [InlineData("file.cc", LanguageIds.Cpp)]
    [InlineData("file.cxx", LanguageIds.Cpp)]
    [InlineData("file.hpp", LanguageIds.Cpp)]
    [InlineData("file.hh", LanguageIds.Cpp)]
    [InlineData("file.hxx", LanguageIds.Cpp)]
    [InlineData("file.cppm", LanguageIds.Cpp)]
    [InlineData("file.ixx", LanguageIds.Cpp)]
    [InlineData("file.mpp", LanguageIds.Cpp)]
    [InlineData("file.cxxm", LanguageIds.Cpp)]
    [InlineData("file.ccm", LanguageIds.Cpp)]
    [InlineData("xmake.lua", LanguageIds.Lua)]
    public void Registers_native_and_Lua_files(string file, string id) => Assert.Equal(id, Language.ForPath(file).Id);

    [Fact]
    public void Validates_clangd_discovery_without_launching_any_program()
    {
        Assert.Equal(new Version(23, 1, 6), Clangd.ReadVersion("clangd version 23.1.6 (LLVM-MinGW)\n"));
        Assert.Throws<ClangdException>(() => Clangd.ReadVersion("another program"));
        Assert.Contains(Clangd.ExecutableVariable, Assert.Throws<ClangdException>(() => Clangd.Executable(null, null)).Message, StringComparison.Ordinal);
        Assert.Throws<ClangdException>(() => Clangd.Executable("relative/clangd", Path.GetTempPath()));
    }

    [Fact]
    public async Task Selects_a_stable_compilation_target_and_recognizes_modules_from_commands()
    {
        string root = PathNames.Canonical(TestFolders.Create("cpp-context-"));
        string source = Path.Combine(root, "shared.cpp");
        string header = Path.Combine(root, "shared.h");
        string file = Path.Combine(root, "compile_commands.json");
        string Command(string name) => $"build/.gens/{name}/shared.pcm";
        string raw = JsonSerializer.Serialize(new[] { "zeta", "alpha" }.Select(name => new
        {
            directory = root, file = source, output = Command(name), arguments = new[] { "clang++", "-x", "c++-module", source },
        }));
        File.WriteAllText(file, raw);
        XmakeTarget Target(string name) => new(name, "static", Path.Combine(root, "xmake.lua"), null,
            [source], [header], [], new(["llvm"], [new("cxx", "clang++", null, null)], null));
        XmakeWorkspace snapshot = new(root, new("xmake", new Version(3, 1, 2), "3.1.2"),
            new("test", "debug", "mingw", "x86_64", root, Path.Combine(root, "xmake.conf"), "llvm"),
            [Target("zeta"), Target("alpha")], new([], ImmutableDictionary<string, ImmutableArray<string>>.Empty),
            [], [], new(file, file), null);
        CppCompilation context = await CppCompilation.ReadAsync(snapshot, TestContext.Current.CancellationToken);
        Assert.Equal("alpha", Assert.Single(context.CommandsFor(source)).Target);
        Assert.Equal("alpha (of 2 Xmake targets)", context.Context(source));
        Assert.True(context.IsModuleInput(source));
        Assert.Equal(LanguageIds.Cpp, context.HeaderLanguage(header, LanguageIds.C));
        Assert.Equal(raw, File.ReadAllText(file));
        Assert.NotEqual(file, context.Database);
    }

    [AvaloniaFact]
    public void Native_and_Lua_syntax_is_available_without_a_server()
    {
        string root = TestFolders.Create("cpp-syntax-");
        TestFolders.Write(root, "unit.cppm", "export module arithmetic;\n\nexport int value() {\n    // return the value\n    return 42;\n}\n");
        TestFolders.Write(root, "xmake.lua", "-- Xmake settings\nif true then\n    set_project('sample')\nend\n");
        using IDisposable waiting = WorkspaceTrust.Ask(root);
        MainWindow window = Ui.OpenFolder(root, languageServer: false, autosaves: false);
        try
        {
            Ui.WaitFor(() => window.AskingTrust is not null, () => "the trust question");
            Ui.ClickButton(window.AskingTrust!, WorkspaceTrustDialog.Untrusted);
            Check("unit.cppm", Language.Cpp, "// return the value");
            Check("xmake.lua", Language.Lua, "-- Xmake settings");
            Assert.Null(window.Session.Cpp?.Server);
        }
        finally
        {
            window.Close();
        }

        void Check(string name, Language language, string comment)
        {
            Assert.Null(window.OpenFile(Path.Combine(root, name)));
            Ui.Settle();
            OpenDocument open = window.EditorArea.Active!;
            Assert.Same(language, open.Language);
            TextSnapshot text = open.Text.Snapshot;
            int at = text.ToString().IndexOf(comment, StringComparison.Ordinal);
            Assert.Contains(open.Highlighter!.GetSpans(text, at, comment.Length), span => span.Kind == TokenKind.Comment);
            Assert.NotEmpty(open.FoldRegions);
        }
    }
}

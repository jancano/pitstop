using System.Collections.Immutable;
using Passenger.Core;
using Passenger.Tests.Debugging.Helpers;
using Pitstop.Templates;
using Pitstop.Xmake;

namespace Passenger.Tests;

public sealed class NativeProjectTemplateTests
{
    [Theory]
    [InlineData("c-console", "c99", "app")]
    [InlineData("c-console", "c23", "app")]
    [InlineData("cpp-console", "c++17", "app", true)]
    [InlineData("cpp-console", "c++20", "app", true)]
    [InlineData("cpp-console", "c++23", "app", true, true)]
    [InlineData("cpp-console", "c++26", "app", true, true)]
    [InlineData("cpp-static-library", "c++23", "project_app_consumer")]
    [InlineData("cpp-static-library", "c++23", "project_app_consumer", true)]
    [InlineData("cpp-shared-library", "c++23", "project_app_consumer")]
    [InlineData("cpp-module-library", "c++23", "project_app_consumer")]
    [InlineData("cpp-multi-target", "c++23", "app", false, true)]
    [InlineData("empty", null, null)]
    public async Task Generates_builds_and_runs_with_the_reference_toolchain(string id, string? standard, string? executable,
        bool modules = false, bool importStd = false)
    {
        string? xmake = Environment.GetEnvironmentVariable(XmakeService.ExecutableVariable);
        string? llvm = Environment.GetEnvironmentVariable("PITSTOP_TEST_LLVM_MINGW");
        Assert.SkipUnless(OperatingSystem.IsWindows() && xmake is not null && File.Exists(xmake) && llvm is not null
            && File.Exists(Path.Combine(llvm, "bin", "clang++.exe")),
            "Native template validation requires the Windows reference Xmake and LLVM-MinGW installations. Set PITSTOP_XMAKE and PITSTOP_TEST_LLVM_MINGW.");
        CancellationToken stop = TestContext.Current.CancellationToken;
        string catalog = Path.Combine(Debuggees.Root, "templates");
        TemplateCatalog discovered = TemplateCatalog.Discover([Path.Combine(catalog, "xmake")], stop);
        Assert.Empty(discovered.Problems);
        ProjectTemplate template = Assert.Single(discovered.Templates, item => item.Manifest.Id == $"pitstop.xmake.{id}");
        using FileStream data = File.OpenRead(Path.Combine(catalog, "catalogs", "language-standards.json"));
        LanguageStandards standards = LanguageStandards.Read(data);
        TemplateEnvironment profile = new(standards, ImmutableHashSet.Create("c99", "c23", "c++17", "c++20", "c++23", "c++26"),
            ImmutableHashSet.Create("cpp.modules", "cpp.std-module"), ImmutableHashSet<string>.Empty);
        Dictionary<string, string> values = new() { ["projectName"] = id };
        if (standard is not null)
        {
            values.Add("standard", standard);
        }
        if (template.Manifest.Parameters.Any(parameter => parameter.Id == "modules"))
        {
            values.Add("modules", modules ? "true" : "false");
        }
        if (template.Manifest.Parameters.Any(parameter => parameter.Id == "stdModule"))
        {
            values.Add("stdModule", importStd ? "true" : "false");
        }
        if (template.Manifest.Parameters.Any(parameter => parameter.Id == "consumer"))
        {
            values.Add("consumer", "true");
        }
        string scratch = TestFolders.Create("pitstop-native-template-");
        string folder = Path.Combine(scratch, "generated");
        try
        {
            GenerationPlan plan = await TemplateGeneration.PlanAsync(template, folder, profile, values, stop);
            await plan.WriteAsync(stop);
            await XmakeAsync(["f", "-m", "debug", "-p", "mingw", "--toolchain=llvm", $"--mingw={llvm}", $"--sdk={llvm}", "--require=no", "-y"]);
            if (executable is null)
            {
                await XmakeAsync(["show", "-l", "targets"]);
                return;
            }
            if (standard == "c++17")
            {
                Assert.False(File.Exists(Path.Combine(folder, "src", "answer.cppm")));
            }
            await XmakeAsync(["build", "-j", "1", "-y"]);
            string output = await XmakeAsync(["run", "-y", executable]);
            Assert.Contains("Pitstop template 42", output, StringComparison.Ordinal);
        }
        finally
        {
            TestFolders.Delete(scratch);
        }

        async Task<string> XmakeAsync(string[] arguments)
        {
            System.Diagnostics.ProcessStartInfo start = ChildProcesses.Command(xmake!, arguments, folder);
            start.Environment["XMAKE_STATS"] = "false";
            start.Environment["XMAKE_COLORTERM"] = "nocolor";
            ChildResult result = await ChildProcesses.RunAsync(start, limit: TimeSpan.FromMinutes(3), cancel: stop);
            Assert.True(result.Succeeded, $"{id} ({standard}): xmake {string.Join(' ', arguments)}\n{result.Output}{result.Error}");
            return result.Output;
        }
    }
}

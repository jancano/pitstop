using System.Collections.Immutable;
using Passenger.App.Settings;
using Passenger.App.State;
using Passenger.Core;
using Pitstop.Cpp;
using Pitstop.Templates;
using Pitstop.Xmake;
using NativeCatalog = Pitstop.Templates.TemplateCatalog;

namespace Passenger.App.Templates;

public sealed record NativeTemplateSetup(NativeCatalog Catalog, TemplateEnvironment Environment, string Toolchain)
{
    public static Task<NativeTemplateSetup> LoadAsync(CancellationToken cancel) =>
        LoadAsync(new NativeToolPaths(), _ => true, cancel);

    internal static async Task<NativeTemplateSetup> LoadAsync(NativeToolPaths tools, Func<string, bool> allowed, CancellationToken cancel)
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "native-templates");
        NativeCatalog catalog = NativeCatalog.Discover([Path.Combine(bundled, "xmake"), Path.Combine(StateStore.DefaultRoot, "templates")], cancel);
        using FileStream data = File.OpenRead(Path.Combine(bundled, "catalogs", "language-standards.json"));
        LanguageStandards standards = LanguageStandards.Read(data);
        TemplateEnvironment pending = new(standards, ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty);
        try
        {
            string scratch = Path.GetTempPath();
            string? xmakePath = NativeToolPaths.Find("xmake", tools.Xmake ?? System.Environment.GetEnvironmentVariable(XmakeService.ExecutableVariable));
            string? llvmBin = tools.LlvmBin ?? System.Environment.GetEnvironmentVariable(Clangd.LlvmBinVariable);
            string? configured = tools.Clangd ?? System.Environment.GetEnvironmentVariable(Clangd.ExecutableVariable);
            string? stock = OperatingSystem.IsWindows()
                ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "LLVM", "bin") : null;
            string? clangdPath = NativeToolPaths.Find("clangd", configured ?? (llvmBin is null ? null : Path.Combine(llvmBin, NativeToolPaths.Tool("clangd"))), stock);
            Guard(xmakePath);
            Guard(clangdPath);
            XmakeInstallation xmake = await new XmakeService(xmakePath).DiscoverAsync(scratch, cancel).ConfigureAwait(false);
            ClangdInstallation llvm = await Clangd.DiscoverAsync(scratch, clangdPath, llvmBin, cancel).ConfigureAwait(false);
            string? clang = tools.Clang is null ? llvm.Clang : NativeToolPaths.Find("clang", tools.Clang);
            Guard(clang);
            if (!OperatingSystem.IsWindows() || xmake.Version != new Version(3, 1, 2) || llvm.Version != new Version(23, 1, 6)
                || clang is null || !llvm.Modules)
            {
                return new(catalog, pending, "Template capabilities are unknown. Select Xmake 3.1.2 and LLVM-MinGW 23.1.6 on Windows x64.");
            }
            ChildResult compiler = await ChildProcesses.RunAsync(ChildProcesses.Command(clang, ["--version"], scratch),
                limit: TimeSpan.FromSeconds(10), cancel: cancel).ConfigureAwait(false);
            if (!compiler.Succeeded || !compiler.Output.Contains("clang version 23.1.6 ", StringComparison.Ordinal)
                || !compiler.Output.Contains("Target: x86_64-w64-windows-gnu", StringComparison.Ordinal))
            {
                return new(catalog, pending, "The clang beside clangd did not identify itself as the Windows x64 LLVM-MinGW 23.1.6 compiler.");
            }
            ImmutableHashSet<string>.Builder supported = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (LanguageStandard standard in standards.Entries.Where(item => item.Language is "c" or "cpp"))
            {
                string language = standard.Language == "c" ? "c" : "c++";
                ChildResult probe = await ChildProcesses.RunAsync(ChildProcesses.Command(clang,
                    ["-x", language, $"-std={standard.Id}", "-Werror", "-fsyntax-only", "-"], scratch),
                    "int main(void) { return 0; }\n", limit: TimeSpan.FromSeconds(10), cancel: cancel).ConfigureAwait(false);
                if (probe.Succeeded)
                {
                    supported.Add(standard.Id);
                }
            }
            string sdk = Path.GetDirectoryName(Path.GetDirectoryName(clang))!;
            ImmutableHashSet<string> features = ImmutableHashSet.Create(StringComparer.Ordinal, "cpp.modules");
            if (File.Exists(Path.Combine(sdk, "share", "libc++", "v1", "std.cppm")))
            {
                features = features.Add("cpp.std-module");
            }
            return new(catalog, new(standards, supported.ToImmutable(), features, ImmutableHashSet.Create("llvm-mingw")),
                "Xmake 3.1.2 · LLVM-MinGW 23.1.6 · Windows x64");
        }
        catch (Exception error) when (error is XmakeException or IOException or ClangdException or TimeoutException)
        {
            return new(catalog, pending, error.Message);
        }

        void Guard(string? tool)
        {
            if (tool is not null && !allowed(PathNames.Canonical(tool)))
            {
                throw new IOException("Trust the current workspace before probing tools it contains.");
            }
        }
    }
}

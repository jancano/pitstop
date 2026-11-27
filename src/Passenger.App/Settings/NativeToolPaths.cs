using System.Text.Json;
using Passenger.Core;
using Pitstop.Cpp;
using Pitstop.Modules;
using Pitstop.Xmake;

namespace Passenger.App.Settings;

public sealed record NativeToolPaths
{
    public string? Xmake { get; init; }
    public string? Xrepo { get; init; }
    public string? LlvmBin { get; init; }
    public string? Clang { get; init; }
    public string? Clangd { get; init; }
    public string? ScanDeps { get; init; }
    public string? Format { get; init; }
    public string? Tidy { get; init; }

    internal static string Tool(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    internal static string? Find(string name, string? explicitPath, params string?[] directories)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.IsPathFullyQualified(explicitPath) && File.Exists(explicitPath) ? PathNames.Canonical(explicitPath)
                : throw new IOException($"Choose the full path of an installed {name} executable. Could not find {explicitPath}.");
        }
        string file = Tool(name);
        return directories.OfType<string>().Where(Path.IsPathFullyQualified).Select(directory => Path.Combine(directory, file))
            .FirstOrDefault(File.Exists) ?? Programs.OnPath(file);
    }

    internal async Task<string> InspectAsync(Func<string, bool> allowed, CancellationToken cancel)
    {
        List<string> report = [];
        string scratch = Directory.CreateTempSubdirectory("pitstop-native-tools-").FullName;
        try
        {
            string? llvm = LlvmBin ?? Environment.GetEnvironmentVariable(Pitstop.Cpp.Clangd.LlvmBinVariable);
            string? xmake = null;
            string? bin = llvm;
            if (llvm is not null && (!Path.IsPathFullyQualified(llvm) || !Directory.Exists(llvm)))
            {
                report.Add("LLVM: enter the full path of the LLVM bin directory.");
            }
            await Examine("Xmake", async () =>
            {
                string program = Choose("xmake", Xmake ?? Environment.GetEnvironmentVariable(XmakeService.ExecutableVariable));
                XmakeInstallation tool = await new XmakeService(program).DiscoverAsync(scratch, cancel).ConfigureAwait(false);
                xmake = tool.Executable;
                return $"{tool.BuildVersion} — {tool.Executable}";
            }).ConfigureAwait(false);
            await Examine("Xrepo", async () =>
            {
                string? directory = xmake is null ? null : Path.GetDirectoryName(xmake);
                string? program = Find("xrepo", Xrepo, directory);
                if (OperatingSystem.IsWindows() && (program is null || Path.GetExtension(program).Equals(".bat", StringComparison.OrdinalIgnoreCase)))
                {
                    string? batch = program ?? (directory is null ? null : Path.Combine(directory, "xrepo.bat"));
                    if (batch is not null && File.Exists(batch) && allowed(PathNames.Canonical(batch)))
                    {
                        string launcher = Choose("xmake", Path.Combine(Path.GetDirectoryName(batch)!, "xmake.exe"));
                        return (await VersionAsync(launcher, ["lua", "private.xrepo", "--version"]).ConfigureAwait(false)) + $" — {batch}";
                    }
                }
                return await BannerAsync("xrepo", program).ConfigureAwait(false);
            }).ConfigureAwait(false);
            await Examine("clangd", async () =>
            {
                string? configured = Clangd ?? Environment.GetEnvironmentVariable(Pitstop.Cpp.Clangd.ExecutableVariable);
                string? stock = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin") : null;
                string program = Choose("clangd", configured ?? (llvm is not null ? Path.Combine(llvm, Tool("clangd")) : null), stock);
                ClangdInstallation tool = await Pitstop.Cpp.Clangd.DiscoverAsync(scratch, program, llvm, cancel).ConfigureAwait(false);
                bin ??= Path.GetDirectoryName(program);
                return $"{tool.Version} — {program} — {(tool.Modules ? "named-module flag available" : "named-module flag unavailable")}";
            }).ConfigureAwait(false);
            await Examine("Clang", () => BannerAsync("clang", Clang, bin)).ConfigureAwait(false);
            await Examine("clang-scan-deps", async () =>
            {
                string program = Choose("clang-scan-deps", ScanDeps ?? Environment.GetEnvironmentVariable(ModuleScanner.ExecutableVariable), bin);
                string banner = await VersionAsync(program, ["--version"]).ConfigureAwait(false);
                string database = Path.Combine(scratch, "compile_commands.json");
                AtomicFile.Write(database, "[]"u8);
                ChildResult scan = await RunAsync(program, ["-format=p1689", "-j", "1", $"-compilation-database={database}"]).ConfigureAwait(false);
                if (scan.Succeeded)
                {
                    using JsonDocument format = JsonDocument.Parse(scan.Output);
                    if (JsonMembers.Value(format.RootElement, "rules") is { ValueKind: JsonValueKind.Array })
                    {
                        return $"{banner} — {program} — P1689 probe succeeded";
                    }
                }
                return $"{banner} — {program} — P1689 probe failed";
            }).ConfigureAwait(false);
            await Examine("clang-format", () => BannerAsync("clang-format", Format, bin)).ConfigureAwait(false);
            await Examine("clang-tidy", () => BannerAsync("clang-tidy", Tidy, bin)).ConfigureAwait(false);
            if (bin is not null && Path.GetDirectoryName(bin) is { } install)
            {
                report.Add($"LLVM installation: {install}");
                string standard = Path.Combine(install, "share", "libc++", "v1", "std.cppm");
                if (File.Exists(standard))
                {
                    report.Add($"Standard library module source: {standard}");
                }
                string sdk = Path.Combine(install, "include", "windows.h");
                if (File.Exists(sdk))
                {
                    report.Add($"MinGW-w64 Windows SDK headers: {Path.GetDirectoryName(sdk)}");
                }
            }
            return string.Join("\n", report);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        string Choose(string name, string? configured, params string?[] directories)
        {
            string program = Find(name, configured, directories) ?? throw new IOException($"Install {name} and add it to PATH or set its absolute path above.");
            return allowed(PathNames.Canonical(program)) ? program : throw new IOException("Trust the workspace before probing tools it contains.");
        }
        async Task<string> BannerAsync(string name, string? configured, params string?[] directories)
        {
            string program = Choose(name, configured, directories);
            return (await VersionAsync(program, ["--version"]).ConfigureAwait(false)) + $" — {program}";
        }
        async Task<string> VersionAsync(string program, string[] arguments)
        {
            ChildResult result = await RunAsync(program, arguments).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new IOException($"Could not read the version from {program}. {result.Error.Trim()}");
            }
            return string.Join(" ", result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3));
        }
        Task<ChildResult> RunAsync(string program, string[] arguments) =>
            ChildProcesses.RunAsync(ChildProcesses.Command(program, arguments, scratch), limit: TimeSpan.FromSeconds(10), cancel: cancel);

        async Task Examine(string name, Func<Task<string>> read)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                report.Add($"{name}: {await read().ConfigureAwait(false)}");
            }
            catch (Exception error) when (error is IOException or XmakeException or ClangdException or TimeoutException
                or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException or JsonException)
            {
                report.Add($"{name}: {error.Message}");
            }
        }
    }
}

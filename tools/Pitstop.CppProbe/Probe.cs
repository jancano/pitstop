using System.Diagnostics;
using System.Text.Json;
using Passenger.Core;

namespace Pitstop.CppProbe;

internal sealed class Probe(ProbeOptions options, CancellationToken cancel)
{
    private static readonly TimeSpan CommandLimit = TimeSpan.FromMinutes(2);
    private static readonly Dictionary<string, string[]> Fixtures = new()
    {
        ["headers"] = ["xmake.lua", "main.cpp", "include/answer.hpp"],
        ["modules"] = ["xmake.lua", "main.cpp", "arithmetic.cppm", "numbers.cppm", "detail.cppm", "implementation.cpp"],
        ["standard-library"] = ["xmake.lua", "main.cpp"],
    };

    internal string Root { get; } = MakeRoot(options.Repository);
    private readonly Dictionary<string, string?> tools = [];
    private readonly List<string> failures = [];

    private static string MakeRoot(string repository)
    {
        string parent = Path.Combine(repository, "artifacts", "cpp-toolchain");
        Directory.CreateDirectory(parent);
        string folder = Path.Combine(parent, Guid.NewGuid().ToString("N")[..12]);
        return Directory.CreateDirectory(folder).FullName;
    }

    internal async Task InventoryAsync()
    {
        Console.WriteLine($"Evidence: {Root}");
        List<object> inventory = [];
        foreach (string name in Program.Tools)
        {
            string? path = FindTool(name);
            tools[name] = path;
            if (path is null)
            {
                inventory.Add(new { name, status = "missing" });
                continue;
            }

            bool usesXmake = name == "xrepo" && tools["xmake"] is not null;
            string[] flags = name switch
            {
                "lldb-dap" => ["--help"],
                "xrepo" when usesXmake => ["lua", "private.xrepo", "--version"],
                _ => ["--version"],
            };
            string program = usesXmake ? tools["xmake"]! : path;
            ChildResult result = await ExecuteAsync(program, flags, Root, name + "-version");
            string? fileVersion = OperatingSystem.IsWindows() ? FileVersionInfo.GetVersionInfo(path).FileVersion : null;
            inventory.Add(new { name, path, fileVersion, result.ExitCode, result.Output, result.Error });
        }
        Program.WriteJson(Path.Combine(Root, "inventory.json"), inventory);
        if (tools["clang"] is { } clang)
        {
            ChildResult library = await ExecuteAsync(clang, ["-v", "-dM", "-E", "-x", "c++", "-std=c++23", "-"],
                Root, "standard-library", "#include <version>\n");
            string[] macros = [.. library.Output.Split('\n').Where(line =>
                line.StartsWith("#define _MSVC_STL_VERSION ", StringComparison.Ordinal) ||
                line.StartsWith("#define _LIBCPP_VERSION ", StringComparison.Ordinal) ||
                line.StartsWith("#define __GLIBCXX__ ", StringComparison.Ordinal))];
            Program.WriteJson(Path.Combine(Root, "standard-library.json"), new { library.ExitCode, macros, searches = library.Error });
        }
    }

    internal async Task RunAsync()
    {
        string xmake = tools["xmake"] ?? throw new InvalidOperationException("Xmake is missing. Pass --xmake with its executable.");
        string? clangd = tools["clangd"];
        foreach ((string fixture, string[] files) in Fixtures)
        {
            foreach (string standard in (string[])["cxx23", "cxx26"])
            {
                string variant = $"{fixture}-{standard}";
                try
                {
                    string folder = CopyFixture(fixture, files, variant, standard);
                    await CheckWorkspaceAsync(xmake, clangd, folder, fixture);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    failures.Add($"{variant}: {error.Message}");
                    Console.Error.WriteLine(failures[^1]);
                }
            }
        }
        Program.WriteJson(Path.Combine(Root, "failures.json"), failures);
        if (failures.Count != 0)
        {
            throw new InvalidOperationException($"{failures.Count} build or probe requirements failed. See {Root}.");
        }
    }

    private string CopyFixture(string name, string[] files, string variant, string standard)
    {
        string source = Path.Combine(options.Repository, "tests", "CppWorkspaces", name);
        string folder = Directory.CreateDirectory(Path.Combine(Root, variant)).FullName;
        foreach (string relative in files)
        {
            string from = Path.GetFullPath(Path.Combine(source, relative));
            if (!PathNames.Same(from, PathNames.Canonical(from)))
            {
                throw new IOException($"The fixture contains a linked input: {from}");
            }
            string to = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to);
        }
        if (standard == "cxx26")
        {
            string project = Path.Combine(folder, "xmake.lua");
            Program.Write(project, File.ReadAllText(project).Replace("set_languages(\"cxx23\")", "set_languages(\"cxx26\")", StringComparison.Ordinal));
        }
        return folder;
    }

    private async Task CheckWorkspaceAsync(string xmake, string? clangd, string folder, string fixture)
    {
        string[] configure = ["f", "-m", "debug", "-y", $"--toolchain={options.Toolchain}",
            .. options.Mingw is null ? Array.Empty<string>() : new[] { "-p", "mingw", $"--mingw={options.Mingw}", $"--sdk={options.Mingw}" }];
        await RequireAsync(xmake, configure, folder, "configure");
        using JsonDocument configuration = await CaptureAsync(xmake, ["show", "--format=json"], folder, "configuration");
        using JsonDocument targetNames = await CaptureAsync(xmake, ["show", "-l", "targets", "--format=json"], folder, "targets");
        foreach (JsonElement entry in targetNames.RootElement.EnumerateArray())
        {
            string name = entry.GetString()!;
            using JsonDocument target = await CaptureAsync(xmake, ["show", "-t", name, "--format=json"], folder, $"target-{name}");
        }
        using JsonDocument graph = await CaptureAsync(xmake, ["show", "--info=depgraph", "--format=json"], folder, "target-graph");
        await RequireAsync(xmake, ["project", "-k", "compile_commands", "--lsp=clangd", "."], folder, "compile-database");
        using JsonDocument database = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "compile_commands.json")));
        if (database.RootElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Xmake returned an empty compilation database.");
        }

        if (clangd is not null)
        {
            await RecordLspAsync(clangd, folder, fixture, "cold", folder, edits: false);
        }

        await RequireAsync(xmake, ["-v", "-y"], folder, "build");
        await RequireAsync(xmake, ["run"], folder, "run");
        if (fixture != "headers")
        {
            await ScanModulesAsync(folder, database.RootElement);
        }
        if (clangd is not null)
        {
            await RecordLspAsync(clangd, folder, fixture, "built", folder, edits: fixture == "modules");
            string adjusted = Directory.CreateDirectory(Path.Combine(folder, "clangd-source-database")).FullName;
            Program.WriteJson(Path.Combine(adjusted, "compile_commands.json"), ScanEntries(database.RootElement));
            await RecordLspAsync(clangd, folder, fixture, "source", adjusted, edits: fixture == "modules");
            if (fixture == "modules")
            {
                await CheckRestartAsync(clangd, folder, adjusted);
            }
        }
        Console.WriteLine($"{Path.GetFileName(folder)} built and ran");
    }

    private async Task CheckRestartAsync(string clangd, string folder, string database)
    {
        string provider = Path.Combine(folder, "arithmetic.cppm");
        string consumer = Path.Combine(folder, "main.cpp");
        string originalProvider = File.ReadAllText(provider);
        string originalConsumer = File.ReadAllText(consumer);
        try
        {
            Program.Write(provider, ClangdChecks.AddExport(originalProvider));
            Program.Write(consumer, ClangdChecks.UseNewExport(originalConsumer));
            await RecordLspAsync(clangd, folder, "modules", "restarted-source", database, edits: false);
        }
        finally
        {
            Program.Write(consumer, originalConsumer);
            Program.Write(provider, originalProvider);
        }
    }

    private async Task ScanModulesAsync(string folder, JsonElement database)
    {
        IEnumerable<string> found = Directory.EnumerateFiles(Path.Combine(folder, "build"), "*.json", SearchOption.AllDirectories)
            .Where(file => Path.GetDirectoryName(Path.GetDirectoryName(file)) is { } parent && Path.GetFileName(parent) == "scans")
            .Order(StringComparer.Ordinal);
        List<object> scans = [];
        foreach (string path in found)
        {
            using JsonDocument parsed = JsonDocument.Parse(File.ReadAllText(path));
            scans.Add(new { path = Path.GetRelativePath(folder, path), data = parsed.RootElement.Clone() });
        }
        Program.WriteJson(Path.Combine(folder, "xmake-module-scans.json"), scans);

        if (tools["clang-scan-deps"] is not { } scanner)
        {
            return;
        }
        Program.WriteJson(Path.Combine(folder, "scan-commands.json"), ScanEntries(database));
        ChildResult scan = await ExecuteAsync(scanner, ["-format=p1689", "-compilation-database=scan-commands.json"], folder, "p1689");
        if (scan.Succeeded)
        {
            using JsonDocument parsed = JsonDocument.Parse(scan.Output);
            Program.WriteJson(Path.Combine(folder, "p1689.json"), parsed.RootElement);
        }
    }

    private static List<object> ScanEntries(JsonElement database)
    {
        // Use source-based dependency discovery and retain the output identity required by P1689 batch mode.
        List<object> entries = [];
        foreach (JsonElement entry in database.EnumerateArray())
        {
            string[] raw = [.. JsonMembers.Texts(entry, "arguments")];
            bool clangCl = raw.Length > 0 && Path.GetFileNameWithoutExtension(raw[0]).Equals("clang-cl", StringComparison.OrdinalIgnoreCase);
            string[] arguments = [.. raw.Where(arg => !arg.StartsWith("-fmodule-file=", StringComparison.Ordinal))
                .Select(arg => arg == "-imsvc" && !clangCl ? "-isystem" : arg)];
            int outputAt = Array.IndexOf(arguments, "-o") + 1;
            if (outputAt > 0 && outputAt < arguments.Length)
            {
                entries.Add(new { directory = JsonMembers.Text(entry, "directory"), file = JsonMembers.Text(entry, "file"), arguments, output = arguments[outputAt] });
            }
        }
        return entries;
    }

    private async Task RecordLspAsync(string clangd, string folder, string fixture, string phase, string database, bool edits)
    {
        using CancellationTokenSource time = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        time.CancelAfter(CommandLimit);
        string logs = Path.Combine(folder, "clangd-" + phase);
        try
        {
            await ClangdChecks.RunAsync(Command(clangd, ["--experimental-modules-support", "--background-index", "--pch-storage=memory", "-j=2",
                "--enable-config=false", $"--compile-commands-dir={database}"], folder), folder, logs, fixture, edits, time.Token);
        }
        catch (Exception error) when (!cancel.IsCancellationRequested)
        {
            Directory.CreateDirectory(logs);
            Program.WriteJson(Path.Combine(logs, "failure.json"), new { error = error.Message });
        }
    }

    private string? FindTool(string name)
    {
        if (name == "xmake" && options.Xmake is { } exact)
        {
            return File.Exists(exact) ? exact : throw new FileNotFoundException("Xmake executable not found.", exact);
        }
        string file = OperatingSystem.IsWindows() ? name + (name == "xrepo" ? ".bat" : ".exe") : name;
        if (name == "xrepo" && tools["xmake"] is { } xmake && File.Exists(Path.Combine(Path.GetDirectoryName(xmake)!, file)))
        {
            return Path.Combine(Path.GetDirectoryName(xmake)!, file);
        }
        string? hostLlvm = OperatingSystem.IsWindows() && name != "xmake"
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin") : null;
        IEnumerable<string> folders = new[] { options.LlvmBin }.OfType<string>()
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            .Concat(new[] { hostLlvm }.OfType<string>());
        return folders.Where(Path.IsPathFullyQualified).Select(folder => Path.Combine(folder, file)).FirstOrDefault(File.Exists);
    }

    private ProcessStartInfo Command(string program, string[] flags, string folder)
    {
        if (folder != Root)
        {
            WorkspaceTrust.Check(folder);
        }

        ProcessStartInfo command = ChildProcesses.Command(program, flags, folder);
        string? llvm = options.LlvmBin ?? Path.GetDirectoryName(tools.GetValueOrDefault("clang") ?? "");
        if (!string.IsNullOrEmpty(llvm))
        {
            command.Environment.TryGetValue("PATH", out string? currentPath);
            command.Environment["PATH"] = llvm + Path.PathSeparator + currentPath;
        }
        command.Environment["XMAKE_COLORTERM"] = "nocolor";
        command.Environment["XMAKE_GLOBALDIR"] = Path.Combine(Root, "xmake-global");
        return command;
    }

    private async Task<ChildResult> ExecuteAsync(string program, string[] flags, string folder, string label, string? input = null)
    {
        ChildResult result = await ChildProcesses.RunAsync(Command(program, flags, folder), input, limit: CommandLimit, cancel: cancel, mostOutput: 4 * 1024 * 1024);
        Program.Write(Path.Combine(folder, label + ".stdout.txt"), result.Output);
        Program.Write(Path.Combine(folder, label + ".stderr.txt"), result.Error);
        Program.WriteJson(Path.Combine(folder, label + ".command.json"), new { program, flags, folder, result.ExitCode });
        return result;
    }

    private async Task<ChildResult> RequireAsync(string program, string[] flags, string folder, string label)
    {
        ChildResult result = await ExecuteAsync(program, flags, folder, label);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{label} exited {result.ExitCode}. See {label}.stdout.txt and {label}.stderr.txt.");
        }
        return result;
    }

    private async Task<JsonDocument> CaptureAsync(string program, string[] flags, string folder, string label)
    {
        ChildResult result = await RequireAsync(program, flags, folder, label);
        JsonDocument parsed = JsonDocument.Parse(result.Output);
        Program.WriteJson(Path.Combine(folder, label + ".json"), parsed.RootElement);
        return parsed;
    }
}

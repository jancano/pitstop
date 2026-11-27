using System.Collections.Immutable;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pitstop.Core;
using Pitstop.ProjectSystem;

namespace Pitstop.Modules;

internal sealed record ScanCommand([property: JsonIgnore] TranslationUnit Unit,
    [property: JsonPropertyName("directory")] string Directory, [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("arguments")] string[] Arguments, [property: JsonPropertyName("output")] string Output)
{
    [JsonIgnore]
    public ImmutableDictionary<string, string> BuildModules { get; init; } = ImmutableDictionary<string, string>.Empty;
}

public sealed class ModuleScanner
{
    public const string ExecutableVariable = "PITSTOP_CLANG_SCAN_DEPS";
    public const string LlvmBinVariable = "PITSTOP_LLVM_BIN";
    private static readonly TimeSpan ScanLimit = TimeSpan.FromMinutes(2);
    private readonly string? executableOverride;
    private readonly string? llvmBin;

    public ModuleScanner() : this(null, null) { }

    public ModuleScanner(string? executableOverride, string? llvmBin)
    {
        this.executableOverride = executableOverride;
        this.llvmBin = llvmBin;
    }

    public async Task<ModuleWorkspace> ScanAsync(INativeWorkspace workspace, CancellationToken stop)
    {
        WorkspaceTrust.Check(workspace.Root);
        if (workspace.CompilationDatabases is not { } databases)
        {
            return Failed(workspace.CompilationDatabaseProblem ?? $"{workspace.ProviderName} did not export compilation commands for this configuration.");
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(databases.Semantic, stop).ConfigureAwait(false));
            (List<ScanCommand> commands, List<string> omitted) = Commands(workspace, json.RootElement, stop);
            if (commands.Count == 0)
            {
                return Failed($"No supported Clang C++ commands are available in {workspace.ProviderName}'s semantic compilation database.");
            }
            using JsonDocument authoritative = JsonDocument.Parse(await File.ReadAllTextAsync(databases.Authoritative, stop).ConfigureAwait(false));
            Dictionary<string, ImmutableDictionary<string, string>> bindings = BuildBindings(workspace.Root, authoritative.RootElement, stop);
            commands = [.. commands.Select(command => command with
            {
                BuildModules = bindings.GetValueOrDefault(command.Unit.Output, ImmutableDictionary<string, string>.Empty),
            })];
            string scanner = Discover(commands);
            string scratch = Directory.CreateTempSubdirectory("pitstop-module-scan-").FullName;
            string input = Path.Combine(scratch, "scan-commands.json");
            try
            {
                ChildResult version = await ChildProcesses.RunAsync(ChildProcesses.Command(scanner, ["--version"], scratch),
                    limit: TimeSpan.FromSeconds(10), cancel: stop).ConfigureAwait(false);
                if (!version.Succeeded || !version.Output.Contains("LLVM version", StringComparison.Ordinal))
                {
                    return Failed($"Could not identify the LLVM scanner at {scanner}. Check {ExecutableVariable}.");
                }
                ChildResult help = await ChildProcesses.RunAsync(ChildProcesses.Command(scanner, ["--help"], scratch),
                    limit: TimeSpan.FromSeconds(10), cancel: stop).ConfigureAwait(false);
                if (!help.Succeeded || !help.Output.Contains("-compilation-database=", StringComparison.Ordinal)
                    || !help.Output.Contains("-format=", StringComparison.Ordinal))
                {
                    return Failed($"The LLVM scanner at {scanner} does not advertise compilation database scanning. Use the reference LLVM-MinGW toolchain.");
                }

                AtomicFile.Write(input, JsonSerializer.SerializeToUtf8Bytes(commands));
                stop.ThrowIfCancellationRequested();
                WorkspaceTrust.Check(workspace.Root);
                ChildResult result = await ChildProcesses.RunAsync(ChildProcesses.Command(scanner,
                    ["-format=p1689", "-j", "2", $"-compilation-database={input}"], workspace.Root),
                    limit: ScanLimit, cancel: stop, mostOutput: 24 * 1024 * 1024).ConfigureAwait(false);
                WorkspaceTrust.Check(workspace.Root);
                string? failure = result.Succeeded ? null : Failure(result);
                ImmutableArray<P1689.Rule> rules = string.IsNullOrWhiteSpace(result.Output) && failure is not null ? [] : P1689.Read(result.Output);
                return P1689.Resolve(workspace, commands, rules, omitted, failure, stop);
            }
            finally
            {
                try
                {
                    File.Delete(input);
                    Directory.Delete(scratch);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    PassengerLog.Write($"Could not remove module scan files at {scratch}. {error.Message}");
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or TimeoutException or Win32Exception or ArgumentException or InvalidOperationException)
        {
            string message = $"Module scanning failed. Check the selected LLVM scanner and {workspace.ProviderName} configuration. {error.Message}";
            PassengerLog.Write(message);
            return Failed(message);
        }

        ModuleWorkspace Failed(string problem) => new(workspace, [], [], [], [], problem);
    }

    private static Dictionary<string, ImmutableDictionary<string, string>> BuildBindings(string root, JsonElement database, CancellationToken stop)
    {
        Dictionary<string, ImmutableDictionary<string, string>> bindings = new(PathNames.Comparer);
        const string prefix = "-fmodule-file=";
        foreach (JsonElement row in database.EnumerateArray())
        {
            stop.ThrowIfCancellationRequested();
            if (JsonMembers.Text(row, "directory") is not { } directory)
            {
                continue;
            }
            string[] args = [.. JsonMembers.Texts(row, "arguments")];
            int output = Array.IndexOf(args, "-o") + 1;
            if (output < 1 || output >= args.Length)
            {
                continue;
            }
            directory = Path.GetFullPath(directory, root);
            ImmutableDictionary<string, string>.Builder named = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (string arg in args.Where(arg => arg.StartsWith(prefix, StringComparison.Ordinal)))
            {
                int between = arg.IndexOf('=', prefix.Length);
                if (between > prefix.Length && between + 1 < arg.Length)
                {
                    named[arg[prefix.Length..between]] = Path.GetFullPath(arg[(between + 1)..], directory);
                }
            }
            if (named.Count > 0)
            {
                bindings[Path.GetFullPath(args[output], directory)] = named.ToImmutable();
            }
        }
        return bindings;
    }

    private static string Failure(ChildResult result)
    {
        string detail = result.Error.Trim();
        PassengerLog.Write($"clang-scan-deps exited {result.ExitCode}. {detail}");
        return $"clang-scan-deps exited {result.ExitCode}. {detail[..Math.Min(detail.Length, 1200)]}";
    }

    private string Discover(List<ScanCommand> commands)
    {
        string tool = OperatingSystem.IsWindows() ? "clang-scan-deps.exe" : "clang-scan-deps";
        if ((executableOverride ?? Environment.GetEnvironmentVariable(ExecutableVariable)) is { Length: > 0 } configured)
        {
            return Path.IsPathFullyQualified(configured) && File.Exists(configured) ? configured
                : throw new IOException($"Set {ExecutableVariable} to the full path to {tool}. The configured file does not exist.");
        }
        IEnumerable<string> bins = commands.Select(command => Path.GetDirectoryName(command.Unit.Context.Compiler)).OfType<string>();
        string? llvm = llvmBin ?? Environment.GetEnvironmentVariable(LlvmBinVariable);
        return bins.Prepend(llvm).OfType<string>().Distinct(PathNames.Comparer).Where(Path.IsPathFullyQualified)
            .Select(bin => Path.Combine(bin, tool)).FirstOrDefault(File.Exists) ?? Programs.OnPath(tool)
            ?? throw new IOException($"Install clang-scan-deps with the configured LLVM toolchain or set {ExecutableVariable}.");
    }

    internal static (List<ScanCommand> Commands, List<string> Omitted) Commands(INativeWorkspace workspace,
        JsonElement database, CancellationToken stop)
    {
        List<ScanCommand> commands = [];
        List<string> omitted = [];
        HashSet<(string Source, ModuleContext Context)> scannedContexts = [];
        ILookup<string, string> owners = workspace.Targets.SelectMany(target => target.SourceFiles.Select(source => (source, target.Name)))
            .ToLookup(row => row.source, row => row.Name, PathNames.Comparer);
        HashSet<string> targets = new(workspace.Targets.Select(target => target.Name), StringComparer.Ordinal);
        NativeConfiguration active = workspace.Configuration;
        string configurationKey = string.Join('|', workspace.ProviderId, workspace.Root, active.Mode, active.Platform, active.Architecture, active.Toolchain);
        foreach (JsonElement row in database.EnumerateArray())
        {
            stop.ThrowIfCancellationRequested();
            if (JsonMembers.Text(row, "directory") is not { } directory || JsonMembers.Text(row, "file") is not { } file)
            {
                omitted.Add("Compilation database row without directory or source");
                continue;
            }
            string[] args = [.. JsonMembers.Texts(row, "arguments")];
            directory = Path.GetFullPath(directory, workspace.Root);
            string source = PathNames.Canonical(Path.GetFullPath(file, directory));
            string compiler = args.FirstOrDefault() ?? string.Empty;
            string driver = Path.GetFileNameWithoutExtension(compiler);
            int outputAt = Array.IndexOf(args, "-o") + 1;
            string? standard = args.FirstOrDefault(arg => arg.StartsWith("-std=c++", StringComparison.Ordinal) || arg.StartsWith("-std=gnu++", StringComparison.Ordinal));
            if (driver.Contains("clang", StringComparison.OrdinalIgnoreCase) && (standard is not null || driver.Contains("++", StringComparison.Ordinal)))
            {
                if (outputAt == 0 || outputAt >= args.Length || !File.Exists(source))
                {
                    omitted.Add(source);
                    continue;
                }
                string output = args[outputAt];
                string[] candidates = [.. owners[source]];
                string? target = candidates.Length == 1 ? candidates[0] : workspace.MatchOutputTarget(output, candidates.Length > 0 ? candidates : targets);
                int language = Array.IndexOf(args, "-x");
                bool moduleJob = language >= 0 && language + 1 < args.Length && args[language + 1] == "c++-module" && args.Contains("--precompile", StringComparer.Ordinal);
                string[] signature = [.. args.Skip(1).Where((arg, index) => index + 1 != outputAt && arg is not ("-o" or "-c" or "--precompile")
                    && !(moduleJob && (index + 1 == language || index == language))
                    && !arg.StartsWith("-fmodule-output", StringComparison.Ordinal))];
                string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', signature))));
                ModuleContext context = new(target, compiler, standard ?? "driver default", key)
                {
                    TargetEvidence = target is null ? ModuleTargetEvidence.Unavailable : candidates.Length == 1
                        ? ModuleTargetEvidence.SourceMembership : ModuleTargetEvidence.XmakeOutputPath,
                    ConfigurationKey = configurationKey,
                };
                if (target is not null && !scannedContexts.Add((source, context)))
                {
                    continue;
                }
                TranslationUnit unit = new(commands.Count, source, Path.GetFullPath(output, directory), context);
                commands.Add(new(unit, directory, file, args, output));
            }
            else if (!Path.GetExtension(source).Equals(".c", StringComparison.OrdinalIgnoreCase))
            {
                omitted.Add(source);
            }
        }
        return (commands, omitted);
    }

}

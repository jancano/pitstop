using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Passenger.Core;

namespace Pitstop.Xmake;

/// <summary>Runs the pinned Xmake 3 JSON commands against a trusted workspace.</summary>
public sealed class XmakeService(string? executable = null)
{
    public const string ProjectFile = "xmake.lua";
    public const string ExecutableVariable = "PITSTOP_XMAKE";
    private static readonly Version MinimumVersion = new(3, 1, 2);
    private static readonly TimeSpan CommandLimit = TimeSpan.FromMinutes(2);

    public static bool IsWorkspace(string folder) => File.Exists(Path.Combine(folder, ProjectFile));

    public async Task<XmakeWorkspace> EvaluateAsync(string root, string outputDirectory, CancellationToken cancel)
    {
        WorkspaceTrust.Check(root);
        Directory.CreateDirectory(outputDirectory);
        XmakeInstallation installation = await DiscoverAsync(outputDirectory, cancel).ConfigureAwait(false);
        try
        {
            return await ReadWorkspaceAsync(installation, root, outputDirectory, cancel).ConfigureAwait(false);
        }
        catch (Exception problem) when (problem is not UntrustedFolderException && problem is
            (JsonException or ArgumentException or InvalidOperationException or IOException))
        {
            throw new XmakeException($"Could not read the evaluated Xmake project. Check {ProjectFile} and reopen the folder. {problem.Message}", problem);
        }
    }

    public async Task<XmakeInstallation> DiscoverAsync(string scratch, CancellationToken cancel)
    {
        string? named = executable ?? Environment.GetEnvironmentVariable(ExecutableVariable);
        string? program = string.IsNullOrWhiteSpace(named)
            ? Programs.OnPath(OperatingSystem.IsWindows() ? "xmake.exe" : "xmake")
            : Path.IsPathFullyQualified(named) && File.Exists(named) ? PathNames.Canonical(named) : null;
        if (program is null)
        {
            throw new XmakeException(string.IsNullOrWhiteSpace(named)
                ? $"Xmake was not found. Install Xmake {MinimumVersion} and add it to PATH, or set {ExecutableVariable} to its executable."
                : $"Xmake was not found at {named}. Set {ExecutableVariable} to the full path of the Xmake executable.");
        }
        program = PathNames.Canonical(program);
        string banner = await RunAsync(program, scratch, "checking the Xmake version", ["--version"], cancel).ConfigureAwait(false);
        return Installation(program, banner);
    }

    internal static XmakeInstallation Installation(string program, string banner)
    {
        const string prefix = "xmake v";
        string? reported = banner.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        string build = reported is null ? string.Empty : reported[prefix.Length..].Split([',', ' ', '\r'], 2)[0];
        string release = build.Split('+', 2)[0];
        if (!Version.TryParse(release, out Version? version) || version.Major != MinimumVersion.Major || version < MinimumVersion)
        {
            throw new XmakeException($"Unsupported Xmake version ({(build.Length == 0 ? "unknown" : build)}) at {program}. Install Xmake {MinimumVersion} or a later 3.x release.");
        }
        return new(program, version, build);
    }

    private static async Task<XmakeWorkspace> ReadWorkspaceAsync(XmakeInstallation installation, string root, string outputDirectory, CancellationToken cancel)
    {
        string projectFile = Path.Combine(root, ProjectFile);
        Task<string> ProjectAsync(string description, string[] arguments, string? input = null)
        {
            cancel.ThrowIfCancellationRequested();
            return RunAsync(ProjectCommand(installation.Executable, root, arguments), description, cancel, input is null ? null : input + "\n");
        }

        await ProjectAsync("configuring the project (check xmake.lua and the installed toolchain)", ["f", "--require=no", "-y"]).ConfigureAwait(false);
        XmakeJson.ProjectInfo info = XmakeJson.ReadCommand<XmakeJson.ShowInfo>(await ProjectAsync("reading the active configuration", ["show", "--format=json"]).ConfigureAwait(false)).Project;
        if (!PathNames.SameFile(projectFile, info.ProjectFile))
        {
            throw new XmakeException($"Xmake reported a different project file at {info.ProjectFile}. Review the project's Xmake configuration.");
        }
        string[] names = XmakeJson.Array<string>(XmakeJson.ReadCommand<JsonElement>(
            await ProjectAsync("listing the targets", ["show", "-l", "targets", "--format=json"]).ConfigureAwait(false)));
        XmakeTargetGraph graph = XmakeJson.Graph(XmakeJson.ReadCommand<XmakeJson.GraphInfo>(
            await ProjectAsync("reading the target dependency graph", ["show", "--info=depgraph", "--format=json"]).ConfigureAwait(false)));
        string metadata = Path.Combine(outputDirectory, "evaluated.json");
        await ProjectAsync("reading evaluated source membership and toolchains", ["lua", "--stdin", "pitstop", metadata], XmakeScripts.Membership).ConfigureAwait(false);
        XmakeJson.Evaluation evaluated = XmakeJson.Read<XmakeJson.Evaluation>(await File.ReadAllTextAsync(metadata, cancel).ConfigureAwait(false));
        Dictionary<string, XmakeJson.EvaluatedTarget> membership = evaluated.Targets.ToDictionary(target => target.Name, StringComparer.Ordinal);
        List<XmakeTarget> targets = [];
        foreach (string name in names.Order(StringComparer.Ordinal))
        {
            if (!membership.TryGetValue(name, out XmakeJson.EvaluatedTarget? files))
            {
                throw new XmakeException($"Xmake's target list changed while reading {name}. Save the project and reopen its folder.");
            }
            string raw = await ProjectAsync($"reading target {name}", ["show", $"--target={name}", "--format=json"]).ConfigureAwait(false);
            targets.Add(XmakeJson.Target(root, name, XmakeJson.ReadCommand<XmakeJson.TargetInfo>(raw), files));
        }

        XmakeCompilationDatabases? databases = null;
        string? databaseProblem = null;
        try
        {
            await ProjectAsync("generating compile_commands.json",
                ["lua", "--stdin", "pitstop", outputDirectory], XmakeScripts.CompilationDatabase).ConfigureAwait(false);
            databases = await WriteSemanticDatabaseAsync(outputDirectory, cancel).ConfigureAwait(false);
        }
        catch (Exception problem) when (problem is XmakeException or IOException or JsonException or UnauthorizedAccessException)
        {
            databaseProblem = $"Compilation database unavailable. {problem.Message}";
            PassengerLog.Write(databaseProblem);
        }
        cancel.ThrowIfCancellationRequested();
        WorkspaceTrust.Check(root);
        return new(root, installation, XmakeJson.Configuration(root, info, evaluated), [.. targets], graph,
            [.. targets.SelectMany(t => t.Packages).Distinct()], XmakeJson.FullPaths(root, [projectFile, .. evaluated.ProjectFiles]), databases, databaseProblem);
    }

    private static async Task<XmakeCompilationDatabases> WriteSemanticDatabaseAsync(string directory, CancellationToken cancel)
    {
        const string filename = "compile_commands.json";
        string original = Path.Combine(directory, filename);
        string semantic = Path.Combine(Directory.CreateDirectory(Path.Combine(directory, "semantic")).FullName, filename);
        string derived = SemanticDatabase(await File.ReadAllTextAsync(original, cancel).ConfigureAwait(false));
        cancel.ThrowIfCancellationRequested();
        AtomicFile.Write(semantic, Encoding.UTF8.GetBytes(derived));
        return new(original, semantic);
    }

    internal static string SemanticDatabase(string original)
    {
        if (JsonNode.Parse(original) is not JsonArray entries)
        {
            throw new JsonException("The compilation database is not an array.");
        }
        foreach (JsonNode? entry in entries)
        {
            if (entry is not JsonObject row)
            {
                throw new JsonException("A compilation database row is not an object.");
            }
            if (row["arguments"] is not JsonArray args || args.Count == 0)
            {
                continue;
            }
            string[] flags = [.. args.Select(arg => arg?.GetValue<string>() ?? throw new JsonException("A compiler argument is missing."))];
            bool clangCl = Path.GetFileNameWithoutExtension(flags[0]).Equals("clang-cl", StringComparison.OrdinalIgnoreCase);
            row["arguments"] = new JsonArray([.. flags.Where(arg => !arg.StartsWith("-fmodule-file=", StringComparison.Ordinal))
                .Select(arg => (JsonNode?)JsonValue.Create(arg == "-imsvc" && !clangCl ? "-isystem" : arg))]);
            int output = Array.IndexOf(flags, "-o") + 1;
            if (output > 0 && output < flags.Length && row["output"] is null)
            {
                row["output"] = flags[output];
            }
        }
        return entries.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    internal static ProcessStartInfo ProjectCommand(string program, string root, string[] arguments)
    {
        string projectFile = Path.Combine(root, ProjectFile);
        WorkspaceTrust.Check(root);
        WorkspaceTrust.Check(projectFile);
        if (!File.Exists(projectFile))
        {
            throw new XmakeException($"{ProjectFile} is missing from {root}. Restore it or open another folder.");
        }
        return Command(program, root, [arguments[0], $"--project={root}", $"--file={projectFile}", .. arguments[1..]]);
    }

    private static ProcessStartInfo Command(string program, string folder, string[] arguments)
    {
        ProcessStartInfo command = ChildProcesses.Command(program, arguments, folder);
        command.Environment["XMAKE_COLORTERM"] = "nocolor";
        command.Environment["XMAKE_STATS"] = "false";
        return command;
    }

    private static Task<string> RunAsync(string program, string folder, string action, string[] arguments, CancellationToken cancel, string? input = null) =>
        RunAsync(Command(program, folder, arguments), action, cancel, input);

    private static async Task<string> RunAsync(ProcessStartInfo command, string action, CancellationToken cancel, string? input = null)
    {
        cancel.ThrowIfCancellationRequested();
        try
        {
            ChildResult result = await ChildProcesses.RunAsync(command, input, limit: CommandLimit, cancel: cancel,
                mostOutput: 8 * 1024 * 1024).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                string detail = (string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim();
                PassengerLog.Write($"Xmake exited {result.ExitCode} while {action} in {command.WorkingDirectory}.\n{result.Output}\n{result.Error}");
                throw new XmakeException($"Xmake exited with code {result.ExitCode} while {action}. {detail[..Math.Min(detail.Length, 1600)]}");
            }
            return result.Output;
        }
        catch (Exception problem) when (problem is IOException or Win32Exception or TimeoutException)
        {
            throw new XmakeException($"Xmake could not finish {action}. {problem.Message}", problem);
        }
    }
}

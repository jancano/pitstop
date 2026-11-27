using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Pitstop.Core;
using Pitstop.ProjectSystem;

namespace Pitstop.Xmake;

public static class XmakeDebugLaunch
{
    private const string Marker = "pitstop-native-launch ";

    // This is Xmake 3.1.2's default-run environment helper. Custom run scripts are inspected for presence only.
    private const string Describe = """
        import("core.base.json")
        import("core.project.config")
        import("core.project.project")
        import("private.action.run.runenvs")

        function main(name)
            config.load()
            local target = assert(project.target(name))
            local function has_hooks(item)
                return item:script("run") ~= nil or item:script("run_before") ~= nil or item:script("run_after") ~= nil
            end
            local hooks = has_hooks(target)
            for _, rule in ipairs(target:orderules()) do
                hooks = hooks or has_hooks(rule)
            end
            local appended, assigned = runenvs.make(target)
            local function records(envs)
                local rows = {}
                for key, values in pairs(envs) do
                    table.insert(rows, {key = key, value = path.joinenv(values)})
                end
                return json.mark_as_array(rows)
            end
            print("pitstop-native-launch " .. json.encode({
                output = path.absolute(target:targetfile()),
                directory = path.absolute(target:rundir()),
                arguments = json.mark_as_array(table.wrap(target:get("runargs"))),
                appended = records(appended),
                assigned = records(assigned),
                runtime_library_paths = json.mark_as_array(appended.PATH or assigned.PATH or {}),
                hooks = hooks,
                mode = config.get("mode"),
                platform = config.get("plat"),
                architecture = config.get("arch"),
                toolchain = config.get("toolchain")
            }))
        end
        """;

    public static async Task<NativeLaunchPlan> ResolveAsync(XmakeWorkspace workspace, string targetName, NativeLaunchOptions options,
        CancellationToken cancel = default)
    {
        WorkspaceTrust.Check(workspace.Root);
        cancel.ThrowIfCancellationRequested();
        XmakeTarget target = workspace.Targets.FirstOrDefault(item => item.Name == targetName && item.Kind == "binary")
            ?? throw new XmakeException("Select an executable target from the evaluated Xmake workspace to debug.");
        if (workspace.Installation.Version != new Version(3, 1, 2))
        {
            throw new XmakeException("The Xmake debug runtime export has been verified with Xmake 3.1.2. Select that version for this probe.");
        }
        if (string.IsNullOrWhiteSpace(target.Output))
        {
            throw new XmakeException("Xmake did not report an executable path for the selected target.");
        }

        ProcessStartInfo start = XmakeService.ProjectCommand(workspace.Installation.Executable, workspace.Root,
            ["lua", "--stdin", "pitstop", targetName]);
        ChildResult result;
        try
        {
            result = await ChildProcesses.RunAsync(start, Describe + "\n", limit: TimeSpan.FromSeconds(30),
                cancel: cancel, mostOutput: 256 * 1024).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            throw new XmakeException("Could not run the Xmake debug runtime export. Check Xmake and the trusted workspace.", error);
        }
        string? line = result.Succeeded ? result.Output.Split('\n').LastOrDefault(row => row.StartsWith(Marker, StringComparison.Ordinal)) : null;
        if (line is null)
        {
            throw new XmakeException($"Xmake did not return debug runtime metadata (exit code {result.ExitCode}). Verify its version and the target configuration.");
        }

        RuntimeMetadata metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<RuntimeMetadata>(line[Marker.Length..], JsonOptions)
                ?? throw new JsonException("Missing runtime object");
        }
        catch (JsonException)
        {
            throw new XmakeException("The Xmake runtime metadata is malformed. Verify the Xmake 3.1.2 installation.");
        }
        cancel.ThrowIfCancellationRequested();
        WorkspaceTrust.Check(workspace.Root);
        if (!PathNames.Same(target.Output, metadata.Output) || metadata.Mode != workspace.Configuration.Mode
            || metadata.Platform != workspace.Configuration.Platform || metadata.Architecture != workspace.Configuration.Architecture
            || metadata.Toolchain != workspace.Configuration.Toolchain)
        {
            throw new XmakeException("The Xmake configuration changed during debug preparation. Refresh the workspace and select the target again.");
        }
        if (metadata.Hooks)
        {
            throw new XmakeException("This target has a custom Xmake run hook. Direct debugging requires a target with the default run behavior.");
        }
        ImmutableDictionary<string, string>.Builder environment = ImmutableDictionary.CreateBuilder<string, string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (options.Environment is { } overrides)
        {
            environment.AddRange(overrides);
        }
        foreach (EnvironmentEntry entry in metadata.Assigned)
        {
            environment[entry.Key] = entry.Value;
        }
        foreach (EnvironmentEntry entry in metadata.Appended)
        {
            string? inherited = environment.TryGetValue(entry.Key, out string? value) ? value : Environment.GetEnvironmentVariable(entry.Key);
            environment[entry.Key] = string.IsNullOrEmpty(inherited) ? entry.Value
                : inherited.StartsWith(entry.Value, StringComparison.Ordinal) ? inherited
                : entry.Value + Path.PathSeparator + inherited;
        }

        return new(workspace, targetName, metadata.Output,
            options.WorkingDirectory is { Length: > 0 } directory ? Path.GetFullPath(directory, workspace.Root) : metadata.Directory,
            options.Arguments.IsDefault ? metadata.Arguments : options.Arguments,
            environment.ToImmutable(), metadata.RuntimeLibraryPaths, options.Console, options.BuildBeforeDebug,
            options.StopOnEntry, options.SourceMaps.IsDefault ? [] : options.SourceMaps);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private sealed record RuntimeMetadata
    {
        public required string Output { get; init; }
        public required string Directory { get; init; }
        public required ImmutableArray<string> Arguments { get; init; }
        public required ImmutableArray<EnvironmentEntry> Appended { get; init; }
        public required ImmutableArray<EnvironmentEntry> Assigned { get; init; }
        public required ImmutableArray<string> RuntimeLibraryPaths { get; init; }
        public required bool Hooks { get; init; }
        public required string Mode { get; init; }
        public required string Platform { get; init; }
        public required string Architecture { get; init; }
        public string? Toolchain { get; init; }
    }

    private sealed record EnvironmentEntry(string Key, string Value);
}

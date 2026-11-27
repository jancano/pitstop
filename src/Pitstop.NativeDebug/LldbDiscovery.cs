using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Pitstop.Core;

namespace Pitstop.NativeDebug;

public enum NativeDebugFailure
{
    MissingTool, InvalidTool, MissingRuntime, UnsupportedTool, Architecture,
    InvalidLaunch, Build, Initialization, Adapter, Timeout, Cleanup,
}

public sealed class NativeDebugException(NativeDebugFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public NativeDebugFailure Failure { get; } = failure;
}

public sealed record LldbInstallation(string Adapter, Version AdapterVersion, Version LldbVersion,
    Architecture? AdapterArchitecture, string? Lldb, string? RuntimeLibrary);

public static class LldbDiscovery
{
    public const string AdapterVariable = "PITSTOP_LLDB_DAP";
    public const string LlvmBinVariable = "PITSTOP_LLVM_BIN";

    public static async Task<LldbInstallation> DiscoverAsync(string? executable = null, string? llvmBin = null,
        CancellationToken cancel = default, Func<string, bool>? mayRun = null)
    {
        string suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        string tool = "lldb-dap" + suffix;
        string? stock = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin", tool)
            : null;
        string path = Select(executable ?? Environment.GetEnvironmentVariable(AdapterVariable),
            llvmBin ?? Environment.GetEnvironmentVariable(LlvmBinVariable), tool, Programs.OnPath(tool), stock);
        if (mayRun?.Invoke(path) == false)
        {
            throw new NativeDebugException(NativeDebugFailure.InvalidTool, "Trust this workspace before inspecting its LLDB installation.");
        }
        Architecture? architecture = ReadArchitecture(path);
        if (architecture is { } found && found != RuntimeInformation.OSArchitecture)
        {
            throw new NativeDebugException(NativeDebugFailure.Architecture,
                $"LLDB adapter architecture {found} does not match the {RuntimeInformation.OSArchitecture} host. Set {AdapterVariable} to a matching adapter.");
        }

        string banner = await AskAsync(path, "--version", cancel).ConfigureAwait(false);
        (Version adapter, Version runtime) = ReadVersions(banner);
        if (adapter < new Version(23, 1, 6) || runtime < new Version(23, 1, 6))
        {
            throw new NativeDebugException(NativeDebugFailure.UnsupportedTool,
                $"LLDB {adapter} with runtime {runtime} is older than the Phase 8 reference. Set {AdapterVariable} to LLDB 23.1.6 or newer.");
        }
        string help = await AskAsync(path, "--help", cancel).ConfigureAwait(false);
        if (!help.Contains("--no-lldbinit", StringComparison.Ordinal)
            || !help.Contains("--pre-init-command", StringComparison.Ordinal)
            || !help.Contains("--repl-mode", StringComparison.Ordinal))
        {
            throw new NativeDebugException(NativeDebugFailure.UnsupportedTool,
                "The adapter lacks the startup options required to disable LLDB init files, configure script loading and control the REPL.");
        }
        string directory = Path.GetDirectoryName(path)!;
        string lldb = Path.Combine(directory, "lldb" + suffix);
        string library = Path.Combine(directory, OperatingSystem.IsWindows() ? "liblldb.dll" : OperatingSystem.IsMacOS() ? "liblldb.dylib" : "liblldb.so");
        return new(path, adapter, runtime, architecture, File.Exists(lldb) ? lldb : null, File.Exists(library) ? library : null);
    }

    internal static string Select(string? explicitly, string? llvmBin, string tool, string? onPath, string? stock)
    {
        if (!string.IsNullOrWhiteSpace(explicitly))
        {
            if (!Path.IsPathFullyQualified(explicitly) || !File.Exists(explicitly))
            {
                throw new NativeDebugException(NativeDebugFailure.MissingTool,
                    $"Set {AdapterVariable} to the absolute path of an installed lldb-dap executable.");
            }
            return PathNames.Canonical(explicitly);
        }
        string? configured = !string.IsNullOrWhiteSpace(llvmBin) && Path.IsPathFullyQualified(llvmBin)
            ? Path.Combine(llvmBin, tool) : null;
        foreach (string? candidate in new[] { configured, onPath, stock })
        {
            if (Path.IsPathFullyQualified(candidate ?? "") && File.Exists(candidate))
            {
                return PathNames.Canonical(candidate);
            }
        }
        throw new NativeDebugException(NativeDebugFailure.MissingTool,
            $"Install an LLVM distribution with lldb-dap or set {AdapterVariable} to its absolute path.");
    }

    internal static (Version Adapter, Version Runtime) ReadVersions(string banner)
    {
        const string adapterHeader = "lldb-dap:";
        const string adapterPrefix = "LLVM version ";
        const string runtimePrefix = "liblldb: lldb version ";
        string[] lines = banner.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Any(line => line.StartsWith(adapterHeader, StringComparison.Ordinal))
            && Read(adapterPrefix) is { } adapter && Read(runtimePrefix) is { } runtime)
        {
            return (adapter, runtime);
        }
        throw new NativeDebugException(NativeDebugFailure.InvalidTool,
            $"The executable did not report both lldb-dap and liblldb versions. Check {AdapterVariable}.");

        Version? Read(string prefix) => lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal)) is { } line
            && Version.TryParse(line[prefix.Length..].Split([' ', '-'], 2)[0], out Version? version) ? version : null;
    }

    internal static Architecture? ReadArchitecture(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        try
        {
            using FileStream file = File.OpenRead(path);
            using PEReader pe = new(file);
            return pe.PEHeaders.CoffHeader.Machine switch
            {
                Machine.Amd64 => Architecture.X64,
                Machine.I386 => Architecture.X86,
                Machine.Arm64 => Architecture.Arm64,
                _ => throw new NativeDebugException(NativeDebugFailure.Architecture, "The executable has an unsupported Windows machine type."),
            };
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            throw new NativeDebugException(NativeDebugFailure.InvalidTool, "The executable is not a readable Windows PE image.", error);
        }
    }

    private static async Task<string> AskAsync(string executable, string option, CancellationToken cancel)
    {
        try
        {
            ProcessStartInfo command = ChildProcesses.Command(executable, [option], Path.GetDirectoryName(executable)!);
            ChildResult result = await ChildProcesses.RunAsync(command, limit: TimeSpan.FromSeconds(10), cancel: cancel,
                mostOutput: 128 * 1024).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return result.Output;
            }
            bool dllMissing = unchecked((uint)result.ExitCode) == 0xc0000135;
            throw new NativeDebugException(dllMissing ? NativeDebugFailure.MissingRuntime : NativeDebugFailure.InvalidTool,
                $"lldb-dap {option} exited with code {result.ExitCode}. Verify that the distribution includes liblldb and all its runtime DLLs.");
        }
        catch (TimeoutException error)
        {
            throw new NativeDebugException(NativeDebugFailure.Timeout, $"lldb-dap {option} timed out. Check {AdapterVariable}.", error);
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException)
        {
            throw new NativeDebugException(NativeDebugFailure.InvalidTool, $"Could not start lldb-dap. Check {AdapterVariable} and its installation.", error);
        }
    }
}

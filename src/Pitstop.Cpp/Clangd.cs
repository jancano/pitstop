using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pitstop.Core;
using Pitstop.Lsp;

namespace Pitstop.Cpp;

public sealed record ClangdInstallation(string Executable, Version Version, bool Modules, string? Clang);

public static class Clangd
{
    public const string ExecutableVariable = "PITSTOP_CLANGD";
    public const string LlvmBinVariable = "PITSTOP_LLVM_BIN";
    private static readonly JsonElement Capabilities = MakeCapabilities();

    public static Task<ClangdInstallation> DiscoverAsync(string safeDirectory, CancellationToken cancel) =>
        DiscoverAsync(safeDirectory, null, null, cancel);

    public static async Task<ClangdInstallation> DiscoverAsync(string safeDirectory, string? executableOverride, string? llvmBin, CancellationToken cancel)
    {
        string suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        string? configured = executableOverride ?? Environment.GetEnvironmentVariable(ExecutableVariable);
        string? llvm = llvmBin ?? Environment.GetEnvironmentVariable(LlvmBinVariable);
        string? onPath = null;
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!string.IsNullOrWhiteSpace(llvm))
            {
                configured = Path.Combine(llvm, "clangd" + suffix);
            }
            else
            {
                onPath = Programs.OnPath("clangd" + suffix);
                if (onPath is null && OperatingSystem.IsWindows())
                {
                    string stock = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin", "clangd.exe");
                    onPath = File.Exists(stock) ? stock : null;
                }
            }
        }
        string executable = Executable(configured, onPath);
        string version = await AskAsync("--version").ConfigureAwait(false);
        string help = await AskAsync("--help-hidden").ConfigureAwait(false);
        string beside = Path.Combine(Path.GetDirectoryName(executable)!, "clang" + suffix);
        return new(executable, ReadVersion(version), help.Contains("--experimental-modules-support", StringComparison.Ordinal),
            File.Exists(beside) ? beside : null);

        async Task<string> AskAsync(string argument)
        {
            ChildResult result = await ChildProcesses.RunAsync(ChildProcesses.Command(executable, [argument], safeDirectory),
                limit: TimeSpan.FromSeconds(10), cancel: cancel, mostOutput: 128 * 1024).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new ClangdException($"clangd could not run {argument} at {executable}. {result.Error.Trim()}");
            }
            return result.Output;
        }
    }

    internal static string Executable(string? configured, string? found)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured) || !File.Exists(configured))
            {
                throw new ClangdException($"Set {ExecutableVariable} to the absolute path of an installed clangd executable. Could not find {configured}.");
            }
            return PathNames.Canonical(configured);
        }
        return found is null ? throw new ClangdException($"Install LLVM with clangd 23.1.6 or set {ExecutableVariable} to its executable.")
            : PathNames.Canonical(found);
    }

    internal static Version ReadVersion(string output)
    {
        const string prefix = "clangd version ";
        string? banner = output.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return banner is not null && Version.TryParse(banner[prefix.Length..].Split([' ', '\r', '-'], 2)[0], out Version? version)
            ? version : throw new ClangdException($"The configured executable did not report a clangd version. Check {ExecutableVariable}.");
    }

    public static Task<LanguageSession> StartAsync(string folder, string database, string logs, CancellationToken cancel) =>
        StartAsync(folder, database, logs, null, null, cancel);

    public static async Task<LanguageSession> StartAsync(string folder, string database, string logs, string? executableOverride, string? llvmBin, CancellationToken cancel)
    {
        WorkspaceTrust.Check(folder);
        Directory.CreateDirectory(logs);
        ClangdInstallation installation = await DiscoverAsync(logs, executableOverride, llvmBin, cancel).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        WorkspaceTrust.Check(folder);
        string[] args = ["--background-index", "--pch-storage=memory", "-j=2", "--enable-config=false",
            $"--compile-commands-dir={Path.GetDirectoryName(database)}", .. installation.Modules ? new[] { "--experimental-modules-support" } : []];
        ProcessStartInfo start = ChildProcesses.Command(installation.Executable, args, folder);
        // clangd only queries compiler executables when a client explicitly provides --query-driver.
        LanguageSession session = await LanguageSession.StartAsync(new ServerStart(start, folder, logs, Capabilities), cancel).ConfigureAwait(false);
        session.Server.RequestLifetime = cancel;
        return session;
    }

    private static JsonElement MakeCapabilities()
    {
        JsonNode capabilities = JsonNode.Parse(ClientCapabilities.Web.GetRawText())!;
        capabilities["textDocument"]!["declaration"] = new JsonObject { ["linkSupport"] = true };
        capabilities["textDocument"]!["semanticTokens"]!["tokenTypes"] = JsonSerializer.SerializeToNode(new[]
        {
            "namespace", "type", "class", "enum", "interface", "struct", "typeParameter", "parameter", "variable", "property",
            "enumMember", "function", "method", "macro", "keyword", "modifier", "comment", "string", "number", "operator", "concept",
        });
        return JsonSerializer.SerializeToElement(capabilities);
    }
}

public sealed class ClangdException(string message) : Exception(message);

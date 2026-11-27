using System.Text;
using System.Text.Json;
using Passenger.Core;

namespace Pitstop.CppProbe;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string[] ToolNames =
        ["xmake", "xrepo", "clang", "clang-cl", "clangd", "clang-scan-deps", "clang-format", "clang-tidy", "lldb", "lldb-dap"];

    internal static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancel = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        try
        {
            ProbeOptions options = ProbeOptions.Read(args);
            Probe probe = new(options, cancel.Token);
            await probe.InventoryAsync();
            if (!options.TrustFixtures)
            {
                Console.WriteLine("Inventory only. Pass --trust-fixtures to evaluate the checked-out C++ fixtures.");
                return 0;
            }

            WorkspaceTrust.Trust([probe.Root]);
            await probe.RunAsync();
            return 0;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    internal static IEnumerable<string> Tools => ToolNames;

    internal static void Write(string path, string value) => AtomicFile.Write(path, Encoding.UTF8.GetBytes(value));

    internal static void WriteJson<T>(string path, T value) => Write(path, JsonSerializer.Serialize(value, Json) + "\n");
}

internal sealed record ProbeOptions(string Repository, string? Xmake, string? LlvmBin, string Toolchain, string? Mingw, bool TrustFixtures)
{
    internal static ProbeOptions Read(string[] arguments)
    {
        string? xmake = null;
        string? llvm = null;
        string? mingw = null;
        string? toolchain = null;
        bool trust = false;
        for (int at = 0; at < arguments.Length; at++)
        {
            string flag = arguments[at];
            switch (flag)
            {
                case "--xmake": xmake = Path.GetFullPath(Value()); break;
                case "--llvm-bin": llvm = Path.GetFullPath(Value()); break;
                case "--toolchain": toolchain = Value(); break;
                case "--mingw": mingw = Path.GetFullPath(Value()); break;
                case "--trust-fixtures": trust = true; break;
                default: throw new ArgumentException($"Unknown option {flag}");
            }

            string Value()
            {
                if (++at >= arguments.Length)
                {
                    throw new ArgumentException($"Missing value for {flag}");
                }
                return arguments[at];
            }
        }

        string repository = Path.GetFullPath(".");
        if (!File.Exists(Path.Combine(repository, "Pitstop.slnx")))
        {
            throw new ArgumentException("Run the probe from the Pitstop repository root.");
        }
        return new(repository, xmake, llvm, toolchain ?? (mingw is null ? "clang" : "llvm"), mingw, trust);
    }
}

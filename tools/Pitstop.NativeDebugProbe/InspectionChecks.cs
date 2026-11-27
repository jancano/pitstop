using System.Text.Json;
using Pitstop.Core;
using Pitstop.Dap;
using Pitstop.NativeDebug;
using Pitstop.ProjectSystem;

namespace Pitstop.NativeDebugProbe;

internal sealed partial class DebugProbe
{
    private async Task InspectImageAsync(NativeLaunchPlan plan, bool symbolsExpected)
    {
        string reader = Path.Combine(llvmRoot, "bin", "llvm-readobj.exe");
        ChildResult image = await ChildProcesses.RunAsync(ChildProcesses.Command(reader, ["--file-headers", "--sections", plan.Executable],
            scratch), limit: TimeSpan.FromSeconds(10), cancel: cancel);
        bool pe = image.Output.Contains("COFF-x86-64", StringComparison.Ordinal);
        bool dwarf = image.Output.Contains(".debug_info", StringComparison.Ordinal);
        bool lines = image.Output.Contains(".debug_line", StringComparison.Ordinal);
        Program.Write(Path.Combine(output, symbolsExpected ? "debug-image.json" : "release-image.json"),
            new { PeCoffX64 = pe, Dwarf = dwarf, DwarfLines = lines });
        Require(image.Succeeded && pe && dwarf == symbolsExpected && lines == symbolsExpected,
            "The fixture's PE/COFF and DWARF sections did not match the active build mode.");
    }

    private async Task InspectReleaseAsync(NativeDebugSession session, NativeLaunchPlan plan, string scenario)
    {
        await session.LaunchAsync(plan, async (adapter, token) =>
        {
            DapBreakpoint point = await BreakpointAsync(adapter, plan.Workspace.Root, "main.cpp", "locals-stop", token);
            Require(!point.Verified, "The Release fixture unexpectedly verified a source breakpoint without DWARF.");
        }, Build(plan), cancel);
        await InspectImageAsync(plan, symbolsExpected: false);
        await UntilExitAsync(session, 31);
    }

    private async Task InspectOptionalAsync(NativeDebugSession session, DapStackFrame frame, DapVariable[] variables)
    {
        await VerifyAsync("libcxx-formatters", async () =>
        {
            string[] names = ["view", "text", "long_text", "unicode", "items", "empty", "nested", "large", "array",
                "ordered", "hashed", "maybe", "either", "tuple", "unique", "shared", "null_pointer"];
            List<object> summaries = [];
            bool vectorFormatted = false;
            foreach (DapVariable variable in variables.Where(item => names.Contains(item.Name, StringComparer.Ordinal)))
            {
                IReadOnlyList<DapVariable> children = variable.VariablesReference == 0 ? [] :
                    (await session.RequestAsync<VariablesResponse>(DapCommands.Variables,
                        new VariablesArguments(variable.VariablesReference, Start: 0, Count: 4), cancel)).Variables;
                bool elements = children.Any(child => child.Name == "[0]");
                vectorFormatted |= variable.Name == "items" && elements && children[0].Value == "2";
                summaries.Add(new
                {
                    variable.Name, variable.Type, IndexedChildren = elements, SampledChildren = children.Count,
                    StringSummary = variable.Value.Contains("debug text", StringComparison.Ordinal),
                    UnicodeSummary = variable.Value.Contains("café λ", StringComparison.Ordinal),
                });
            }
            Program.Write(Path.Combine(output, "libcxx.json"), summaries);
            Require(vectorFormatted && variables.Any(value => value.Name == "text" && value.Value.Contains("debug text", StringComparison.Ordinal)),
                "libc++ vector synthetic elements or the string content summary were unavailable.");
        }, optional: true);
        await VerifyAsync("registers", async () =>
        {
            ScopesResponse scopes = await session.RequestAsync<ScopesResponse>(DapCommands.Scopes, new ScopesArguments(frame.Id), cancel);
            DapScope registers = scopes.Scopes.Single(scope => scope.Name == "Registers");
            VariablesResponse values = await session.RequestAsync<VariablesResponse>(DapCommands.Variables, new VariablesArguments(registers.VariablesReference), cancel);
            Require(values.Variables.Count > 0, "The register scope returned no groups.");
        }, optional: true);
        await VerifyAsync("loaded-images", async () =>
        {
            JsonElement modules = await session.RequestAsync<JsonElement>(DapCommands.Modules, new { startModule = 0, moduleCount = 64 }, cancel);
            Require(JsonMembers.Items(modules, "modules").Any(module =>
                JsonMembers.Text(module, "name")?.Contains("debug-library", StringComparison.OrdinalIgnoreCase) == true),
                "The loaded binary module list did not include the fixture DLL.");
        }, optional: true);
        await VerifyAsync("read-memory-and-disassemble", async () =>
        {
            Require(frame.InstructionPointerReference is { Length: > 0 }, "The source frame has no instruction address.");
            JsonElement view = await session.RequestAsync<JsonElement>(DapCommands.Disassemble,
                new { memoryReference = frame.InstructionPointerReference, instructionCount = 4 }, cancel);
            JsonElement memory = await session.RequestAsync<JsonElement>(DapCommands.ReadMemory,
                new { memoryReference = frame.InstructionPointerReference, count = 8 }, cancel);
            Require(JsonMembers.Items(view, "instructions").Count == 4
                && Convert.FromBase64String(JsonMembers.Text(memory, "data") ?? "").Length == 8,
                "LLDB returned incomplete disassembly or instruction memory.");
        }, optional: true);
    }
}

using System.Text.Json;
using Pitstop.Core;
using Pitstop.Dap;

if (args is ["--version"])
{
    Console.WriteLine("lldb-dap:\nLLVM version 23.1.6\nliblldb: lldb version 23.1.6");
    return;
}
if (args is ["--help"])
{
    Console.WriteLine("--no-lldbinit --pre-init-command --repl-mode");
    return;
}

string mode = File.ReadAllText("mode");
bool deferred = mode == "deferred";
int? attachedPid = null;
TaskCompletionSource harnessConfigured = new(TaskCreationOptions.RunContinuationsAsynchronously);
TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
await using DapConnection adapter = new(Console.OpenStandardInput(), Console.OpenStandardOutput());
adapter.Handle<JsonElement, DapCapabilities>(DapCommands.Initialize, request =>
    JsonMembers.Text(request, "adapterID") == "lldb-dap" && JsonMembers.Text(request, "clientID") == "pitstop"
        ? Task.FromResult(new DapCapabilities { SupportsConfigurationDoneRequest = true })
        : throw new InvalidOperationException("The initialize request must use DAP's ID casing."),
    () =>
    {
        if (mode == "crash")
        {
            Environment.ExitCode = 86;
            finished.TrySetResult();
        }
    });
adapter.Handle<JsonElement>(DapCommands.Launch, async request =>
{
    if (mode == "timeout")
    {
        await harnessConfigured.Task;
        return;
    }
    if (!JsonMembers.Texts(request, "args").SequenceEqual(["a b", "a\"b", "", "café"])
        || JsonMembers.Text(JsonMembers.Object(request, "env"), "PITSTOP_FIXTURE") != "test"
        || JsonMembers.Items(request, "sourceMap").Count != 1)
    {
        throw new InvalidOperationException("Unexpected launch arguments.");
    }
    if (deferred)
    {
        adapter.SendEvent(DapEvents.Initialized);
        await harnessConfigured.Task;
    }
}, () =>
{
    if (!deferred && mode != "timeout")
    {
        adapter.SendEvent(DapEvents.Initialized);
    }
});
adapter.Handle<JsonElement>(DapCommands.Attach, request =>
{
    attachedPid = JsonMembers.Number(request, "pid");
    if (attachedPid is null || JsonMembers.Bool(request, "stopOnEntry") != true)
    {
        throw new InvalidOperationException("Attach must select an explicit process and stop before resuming.");
    }
    return Task.CompletedTask;
}, () => adapter.SendEvent(DapEvents.Initialized));
adapter.Handle<JsonElement>(DapCommands.ConfigurationDone, _ =>
{
    harnessConfigured.TrySetResult();
    return Task.CompletedTask;
}, () => adapter.SendEvent(DapEvents.Stopped, new StoppedEvent { Reason = StopReasons.Entry, ThreadId = 2 }));
adapter.Handle<DisconnectArguments>(DapCommands.Disconnect, request =>
{
    if (request.TerminateDebuggee != (attachedPid is null))
    {
        throw new InvalidOperationException("Wrong ownership policy for this harness request.");
    }
    File.WriteAllText("disconnect", attachedPid is null ? "terminate" : "detach");
    return Task.CompletedTask;
}, () => finished.TrySetResult());
adapter.Start();
await Task.WhenAny(adapter.Completion, finished.Task);

namespace Pitstop.App.Debugging;

public sealed class NativeFunctionBreakpoint(string name) : Breakpoint
{
    public string Name { get; } = name;
    public override string Location => Name;
}

public sealed class NativeCppThrowBreakpoint : Breakpoint
{
    internal const string Filter = "cpp_throw";

    public NativeCppThrowBreakpoint() => Enabled = false;

    public override string Location => "C++ exception thrown";
}

public sealed record KeptNativeFunctionBreakpoint(string Name, bool Enabled);

public sealed partial class BreakpointStore
{
    private readonly List<NativeFunctionBreakpoint> nativeFunctions = [];
    public IReadOnlyList<NativeFunctionBreakpoint> NativeFunctions => nativeFunctions;
    public NativeCppThrowBreakpoint NativeCppThrow { get; } = new();
    internal List<string> NativeWatchExpressions { get; } = [];
    public event EventHandler? NativeBreakpointsChanged;
    public event EventHandler? NativeWatchesChanged;

    public NativeFunctionBreakpoint AddNativeFunction(string name)
    {
        string symbol = name.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (nativeFunctions.FirstOrDefault(point => point.Name == symbol) is { } found)
        {
            return found;
        }
        NativeFunctionBreakpoint point = new(symbol);
        nativeFunctions.Add(point);
        NativeBreakpointsChanged?.Invoke(this, EventArgs.Empty);
        return point;
    }

    internal void NativeWatchesEdited() => NativeWatchesChanged?.Invoke(this, EventArgs.Empty);

    private void NotifyNativeBreakpoints() => NativeBreakpointsChanged?.Invoke(this, EventArgs.Empty);
}

#nullable enable

namespace Pitstop.Debugger.Interop;

public sealed class BeforeGarbageCollectionCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugProcess Process { get; }

    public BeforeGarbageCollectionCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess)
    {
        Process = pProcess;
    }
}

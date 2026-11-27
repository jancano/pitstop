#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugHeapValue2Extensions
{
    extension(ICorDebugHeapValue2 instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugHeapValue2.TryCreateHandle(Pitstop.Debugger.Interop.CorDebugHandleType,Pitstop.Debugger.Interop.ICorDebugHandleValue@)" />
        public ICorDebugHandleValue CreateHandle(CorDebugHandleType type)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateHandle(type, out ICorDebugHandleValue ppHandle));
            return ppHandle;
        }
    }
}

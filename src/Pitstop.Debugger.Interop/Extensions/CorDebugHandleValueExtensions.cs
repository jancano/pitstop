#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugHandleValueExtensions
{
    extension(ICorDebugHandleValue instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugHandleValue.TryDispose" />
        public void Dispose()
        {
            Marshal.ThrowExceptionForHR(instance.TryDispose());
        }
    }
}

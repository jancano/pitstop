#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugGenericValueExtensions
{
    extension(ICorDebugGenericValue instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugGenericValue.TryGetValue(System.IntPtr)" />
        public void GetValue(nint pTo)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetValue(pTo));
        }
    }
}

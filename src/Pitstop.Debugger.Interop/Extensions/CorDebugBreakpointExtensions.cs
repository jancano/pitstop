#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugBreakpointExtensions
{
    extension(ICorDebugBreakpoint instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugBreakpoint.TryActivate(System.Boolean)" />
        public void Activate(bool bActive)
        {
            Marshal.ThrowExceptionForHR(instance.TryActivate(bActive));
        }
    }
}

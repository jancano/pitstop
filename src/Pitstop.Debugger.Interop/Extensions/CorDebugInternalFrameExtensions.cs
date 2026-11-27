#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugInternalFrameExtensions
{
    extension(ICorDebugInternalFrame instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugInternalFrame.TryGetFrameType(Pitstop.Debugger.Interop.CorDebugInternalFrameType@)" />
        public CorDebugInternalFrameType FrameType
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetFrameType(out var pType));
                return pType;
            }
        }
    }
}

#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugAppDomain2Extensions
{
    extension(ICorDebugAppDomain2 instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugAppDomain2.TryGetArrayOrPointerType(Pitstop.Debugger.Interop.CorElementType,System.UInt32,Pitstop.Debugger.Interop.ICorDebugType,Pitstop.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType GetArrayOrPointerType(CorElementType elementType, int nRank, ICorDebugType pTypeArg)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetArrayOrPointerType(elementType, checked((uint)nRank), pTypeArg, out ICorDebugType ppType));
            return ppType;
        }
    }
}

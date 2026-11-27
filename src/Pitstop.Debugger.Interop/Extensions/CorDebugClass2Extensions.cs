#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugClass2Extensions
{
    extension(ICorDebugClass2 instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugClass2.TryGetParameterizedType(Pitstop.Debugger.Interop.CorElementType,System.UInt32,Pitstop.Debugger.Interop.ICorDebugType[],Pitstop.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType GetParameterizedType(CorElementType elementType, int nTypeArgs, ICorDebugType[] ppTypeArgs)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetParameterizedType(elementType, checked((uint)nTypeArgs), ppTypeArgs, out ICorDebugType ppType));
            return ppType;
        }
    }
}

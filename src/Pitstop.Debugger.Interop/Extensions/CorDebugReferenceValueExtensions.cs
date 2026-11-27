#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugReferenceValueExtensions
{
    extension(ICorDebugReferenceValue instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugReferenceValue.TryDereference(Pitstop.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue Dereference()
        {
            Marshal.ThrowExceptionForHR(instance.TryDereference(out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugReferenceValue.TryGetValue(Pitstop.Debugger.Interop.CORDB_ADDRESS@)" />
        public CORDB_ADDRESS Value
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetValue(out var pValue));
                return pValue;
            }
            set
            {
                Marshal.ThrowExceptionForHR(instance.TrySetValue(value));
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugReferenceValue.TryIsNull(System.Boolean@)" />
        public bool IsNull
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsNull(out var pbNull));
                return pbNull;
            }
        }
    }
}

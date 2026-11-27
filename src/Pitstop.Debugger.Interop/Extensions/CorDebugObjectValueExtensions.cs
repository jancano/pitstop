#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugObjectValueExtensions
{
    extension(ICorDebugObjectValue instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugObjectValue.TryGetClass(Pitstop.Debugger.Interop.ICorDebugClass@)" />
        public ICorDebugClass Class
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetClass(out ICorDebugClass ppClass));
                return ppClass;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugObjectValue.TryGetFieldValue(Pitstop.Debugger.Interop.ICorDebugClass,Pitstop.Debugger.Interop.mdFieldDef,Pitstop.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetFieldValue(ICorDebugClass pClass, mdFieldDef fieldDef)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetFieldValue(pClass, fieldDef, out ICorDebugValue ppValue));
            return ppValue;
        }
    }
}

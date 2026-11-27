#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugValueValue2Extensions
{
    extension(ICorDebugValue instance)
    {
        /// <inheritdoc cref="T:Pitstop.Debugger.Interop.ICorDebugValue2" />
        public ICorDebugValue2 Value2
        {
            get
            {
                return (instance as ICorDebugValue2) ?? throw new NotSupportedException("ICorDebugValue does not support ICorDebugValue2.");
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugValue2.TryGetExactType(Pitstop.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType ExactType
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Value2.TryGetExactType(out ICorDebugType ppType));
                return ppType;
            }
        }
    }
}

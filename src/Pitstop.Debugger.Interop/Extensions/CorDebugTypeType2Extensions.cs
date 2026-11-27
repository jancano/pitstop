#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugTypeType2Extensions
{
    extension(ICorDebugType instance)
    {
        /// <inheritdoc cref="T:Pitstop.Debugger.Interop.ICorDebugType2" />
        public ICorDebugType2 Type2
        {
            get
            {
                return (instance as ICorDebugType2) ?? throw new NotSupportedException("ICorDebugType does not support ICorDebugType2.");
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugType2.TryGetTypeID(Pitstop.Debugger.Interop.COR_TYPEID@)" />
        public int TryGetTypeID(out COR_TYPEID id)
        {
            return instance.Type2.TryGetTypeID(out id);
        }
    }
}

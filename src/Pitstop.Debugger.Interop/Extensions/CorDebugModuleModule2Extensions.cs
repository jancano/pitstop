#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugModuleModule2Extensions
{
    extension(ICorDebugModule instance)
    {
        /// <inheritdoc cref="T:Pitstop.Debugger.Interop.ICorDebugModule2" />
        public ICorDebugModule2 Module2
        {
            get
            {
                return (instance as ICorDebugModule2) ?? throw new NotSupportedException("ICorDebugModule does not support ICorDebugModule2.");
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugModule2.TryGetJITCompilerFlags(Pitstop.Debugger.Interop.CorDebugJITCompilerFlags@)" />
        public CorDebugJITCompilerFlags JITCompilerFlags
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Module2.TryGetJITCompilerFlags(out var pdwFlags));
                return pdwFlags;
            }
            set
            {
                Marshal.ThrowExceptionForHR(instance.Module2.TrySetJITCompilerFlags(value));
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugModule2.TrySetJMCStatus(System.Boolean,System.UInt32,Pitstop.Debugger.Interop.mdToken[])" />
        public void SetJMCStatus(bool bIsJustMyCode, int cTokens, mdToken[] pTokens)
        {
            instance.Module2.SetJMCStatus(bIsJustMyCode, cTokens, pTokens);
        }
    }
}

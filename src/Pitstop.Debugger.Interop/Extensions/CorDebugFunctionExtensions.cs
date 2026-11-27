#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugFunctionExtensions
{
    extension(ICorDebugFunction instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugFunction.TryGetClass(Pitstop.Debugger.Interop.ICorDebugClass@)" />
        public ICorDebugClass Class
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetClass(out ICorDebugClass ppClass));
                return ppClass;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugFunction.TryGetILCode(Pitstop.Debugger.Interop.ICorDebugCode@)" />
        public ICorDebugCode ILCode
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetILCode(out ICorDebugCode ppCode));
                return ppCode;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugFunction.TryGetLocalVarSigToken(Pitstop.Debugger.Interop.mdSignature@)" />
        public mdSignature LocalVarSigToken
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetLocalVarSigToken(out var pmdSig));
                return pmdSig;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugFunction.TryGetModule(Pitstop.Debugger.Interop.ICorDebugModule@)" />
        public ICorDebugModule Module
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule ppModule));
                return ppModule;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugFunction.TryGetToken(Pitstop.Debugger.Interop.mdMethodDef@)" />
        public mdMethodDef Token
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pMethodDef));
                return pMethodDef;
            }
        }
    }
}

#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugEvalExtensions
{
    extension(ICorDebugEval instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugEval.TryCreateValue(Pitstop.Debugger.Interop.CorElementType,Pitstop.Debugger.Interop.ICorDebugClass,Pitstop.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue CreateValue(CorElementType elementType, ICorDebugClass? pElementClass)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateValue(elementType, pElementClass, out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugEval.TryGetResult(Pitstop.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue Result
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetResult(out ICorDebugValue ppResult));
                return ppResult;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugEval.TryGetThread(Pitstop.Debugger.Interop.ICorDebugThread@)" />
        public ICorDebugThread Thread
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetThread(out ICorDebugThread ppThread));
                return ppThread;
            }
        }
    }
}

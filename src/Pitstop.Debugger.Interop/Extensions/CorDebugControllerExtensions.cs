#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugControllerExtensions
{
    extension(ICorDebugController instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugController.TryIsRunning(System.Boolean@)" />
        public bool IsRunning
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsRunning(out var pbRunning));
                return pbRunning;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugController.TryContinue(System.Boolean)" />
        public void Continue(bool fIsOutOfBand)
        {
            Marshal.ThrowExceptionForHR(instance.TryContinue(fIsOutOfBand));
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugController.TryEnumerateThreads(Pitstop.Debugger.Interop.ICorDebugThreadEnum@)" />
        public IEnumerable<ICorDebugThread> EnumerateThreads()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateThreads(out ICorDebugThreadEnum ppThreads));
            return CorDebugEnums.Walk<ICorDebugThread>(ppThreads.TryNext);
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugController.TrySetAllThreadsDebugState(Pitstop.Debugger.Interop.CorDebugThreadState,Pitstop.Debugger.Interop.ICorDebugThread)" />
        public void SetAllThreadsDebugState(CorDebugThreadState state, ICorDebugThread pExceptThisThread)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetAllThreadsDebugState(state, pExceptThisThread));
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugController.TryStop(System.UInt32)" />
        public void Stop(int dwTimeoutIgnored)
        {
            Marshal.ThrowExceptionForHR(instance.TryStop(checked((uint)dwTimeoutIgnored)));
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugController.TryTerminate(System.UInt32)" />
        public void Terminate(int exitCode)
        {
            Marshal.ThrowExceptionForHR(instance.TryTerminate(checked((uint)exitCode)));
        }
    }
}

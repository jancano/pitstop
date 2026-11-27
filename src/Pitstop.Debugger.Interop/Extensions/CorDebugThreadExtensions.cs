#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugThreadExtensions
{
    extension(ICorDebugThread instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugThread.TryEnumerateChains(Pitstop.Debugger.Interop.ICorDebugChainEnum@)" />
        public IEnumerable<ICorDebugChain> EnumerateChains()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateChains(out ICorDebugChainEnum ppChains));
            return CorDebugEnums.Walk<ICorDebugChain>(ppChains.TryNext);
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugThread.TryCreateEval(Pitstop.Debugger.Interop.ICorDebugEval@)" />
        public ICorDebugEval CreateEval()
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateEval(out ICorDebugEval ppEval));
            return ppEval;
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugThread.TryCreateStepper(Pitstop.Debugger.Interop.ICorDebugStepper@)" />
        public ICorDebugStepper CreateStepper()
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateStepper(out ICorDebugStepper ppStepper));
            return ppStepper;
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugThread.TryGetActiveFrame(Pitstop.Debugger.Interop.ICorDebugFrame@)" />
        public ICorDebugFrame ActiveFrame
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetActiveFrame(out ICorDebugFrame ppFrame));
                return ppFrame;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugThread.TryGetAppDomain(Pitstop.Debugger.Interop.ICorDebugAppDomain@)" />
        public ICorDebugAppDomain AppDomain
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetAppDomain(out ICorDebugAppDomain ppAppDomain));
                return ppAppDomain;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugThread.TryGetID(System.UInt32@)" />
        public int Id
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetID(out var pdwThreadId));
                return checked((int)pdwThreadId);
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugThread.TryGetProcess(Pitstop.Debugger.Interop.ICorDebugProcess@)" />
        public ICorDebugProcess Process
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetProcess(out ICorDebugProcess ppProcess));
                return ppProcess;
            }
        }
    }
}

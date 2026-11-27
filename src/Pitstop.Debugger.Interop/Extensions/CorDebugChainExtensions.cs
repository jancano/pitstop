#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugChainExtensions
{
    extension(ICorDebugChain instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugChain.TryEnumerateFrames(Pitstop.Debugger.Interop.ICorDebugFrameEnum@)" />
        public IEnumerable<ICorDebugFrame> EnumerateFrames()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateFrames(out ICorDebugFrameEnum ppFrames));
            return CorDebugEnums.Walk<ICorDebugFrame>(ppFrames.TryNext);
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugChain.TryGetReason(Pitstop.Debugger.Interop.CorDebugChainReason@)" />
        public CorDebugChainReason Reason
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetReason(out var pReason));
                return pReason;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugChain.TryGetThread(Pitstop.Debugger.Interop.ICorDebugThread@)" />
        public ICorDebugThread Thread
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetThread(out ICorDebugThread ppThread));
                return ppThread;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugChain.TryIsManaged(System.Boolean@)" />
        public bool IsManaged
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsManaged(out var pManaged));
                return pManaged;
            }
        }
    }
}

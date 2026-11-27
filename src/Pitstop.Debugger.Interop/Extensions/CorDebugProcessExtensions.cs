#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugProcessExtensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugProcess.TryEnumerateAppDomains(Pitstop.Debugger.Interop.ICorDebugAppDomainEnum@)" />
        public IEnumerable<ICorDebugAppDomain> EnumerateAppDomains()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateAppDomains(out ICorDebugAppDomainEnum ppAppDomains));
            return CorDebugEnums.Walk<ICorDebugAppDomain>(ppAppDomains.TryNext);
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugProcess.TryReadMemory(Pitstop.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.Byte[],System.UIntPtr@)" />
        public (byte[] buffer, nuint read) ReadMemory(CORDB_ADDRESS address, int size)
        {
            byte[] array = new byte[size];
            Marshal.ThrowExceptionForHR(instance.TryReadMemory(address, checked((uint)size), array, out var read));
            return (buffer: array, read: read);
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugProcess.TryGetThread(System.UInt32,Pitstop.Debugger.Interop.ICorDebugThread@)" />
        public ICorDebugThread GetThread(int dwThreadId)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetThread(checked((uint)dwThreadId), out ICorDebugThread ppThread));
            return ppThread;
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugProcess.TryWriteMemory(Pitstop.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.Byte[],System.UIntPtr@)" />
        public nuint WriteMemory(CORDB_ADDRESS address, int size, byte[] buffer)
        {
            Marshal.ThrowExceptionForHR(instance.TryWriteMemory(address, checked((uint)size), buffer, out var written));
            return written;
        }
    }
}

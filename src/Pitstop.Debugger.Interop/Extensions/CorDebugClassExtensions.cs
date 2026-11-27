#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugClassExtensions
{
    extension(ICorDebugClass instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugClass.TryGetModule(Pitstop.Debugger.Interop.ICorDebugModule@)" />
        public ICorDebugModule Module
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule pModule));
                return pModule;
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugClass.TryGetToken(Pitstop.Debugger.Interop.mdTypeDef@)" />
        public mdTypeDef Token
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pTypeDef));
                return pTypeDef;
            }
        }
    }
}

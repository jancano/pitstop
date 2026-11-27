#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugBoxValueExtensions
{
    extension(ICorDebugBoxValue instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugBoxValue.TryGetObject(Pitstop.Debugger.Interop.ICorDebugObjectValue@)" />
        public ICorDebugObjectValue Object
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetObject(out ICorDebugObjectValue ppObject));
                return ppObject;
            }
        }
    }
}

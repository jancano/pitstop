#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugDelegateObjectValueExtensions
{
    extension(ICorDebugDelegateObjectValue instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugDelegateObjectValue.TryGetTarget(Pitstop.Debugger.Interop.ICorDebugReferenceValue@)" />
        public ICorDebugReferenceValue Target
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetTarget(out ICorDebugReferenceValue ppObject));
                return ppObject;
            }
        }
    }
}

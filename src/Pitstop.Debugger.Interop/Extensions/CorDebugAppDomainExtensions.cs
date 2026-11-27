#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugAppDomainExtensions
{
    extension(ICorDebugAppDomain instance)
    {
        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugAppDomain.TryEnumerateSteppers(Pitstop.Debugger.Interop.ICorDebugStepperEnum@)" />
        public IEnumerable<ICorDebugStepper> EnumerateSteppers()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateSteppers(out ICorDebugStepperEnum ppSteppers));
            return CorDebugEnums.Walk<ICorDebugStepper>(ppSteppers.TryNext);
        }
    }
}

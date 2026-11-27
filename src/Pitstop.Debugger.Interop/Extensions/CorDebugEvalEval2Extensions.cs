#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace Pitstop.Debugger.Interop.Extensions;

public static class CorDebugEvalEval2Extensions
{
    extension(ICorDebugEval instance)
    {
        /// <inheritdoc cref="T:Pitstop.Debugger.Interop.ICorDebugEval2" />
        public ICorDebugEval2 Eval2
        {
            get
            {
                return (instance as ICorDebugEval2) ?? throw new NotSupportedException("ICorDebugEval does not support ICorDebugEval2.");
            }
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugEval2.TryCallParameterizedFunction(Pitstop.Debugger.Interop.ICorDebugFunction,System.UInt32,Pitstop.Debugger.Interop.ICorDebugType[],System.UInt32,Pitstop.Debugger.Interop.ICorDebugValue[])" />
        public void CallParameterizedFunction(ICorDebugFunction pFunction, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs)
        {
            instance.Eval2.CallParameterizedFunction(pFunction, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugEval2.TryNewParameterizedArray(Pitstop.Debugger.Interop.ICorDebugType,System.UInt32,System.UInt32[],System.UInt32[])" />
        public void NewParameterizedArray(ICorDebugType pElementType, int rank, uint[] dims, uint[] lowBounds)
        {
            instance.Eval2.NewParameterizedArray(pElementType, rank, dims, lowBounds);
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugEval2.TryNewParameterizedObject(Pitstop.Debugger.Interop.ICorDebugFunction,System.UInt32,Pitstop.Debugger.Interop.ICorDebugType[],System.UInt32,Pitstop.Debugger.Interop.ICorDebugValue[])" />
        public void NewParameterizedObject(ICorDebugFunction pConstructor, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs)
        {
            instance.Eval2.NewParameterizedObject(pConstructor, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugEval2.TryNewParameterizedObjectNoConstructor(Pitstop.Debugger.Interop.ICorDebugClass,System.UInt32,Pitstop.Debugger.Interop.ICorDebugType[])" />
        public void NewParameterizedObjectNoConstructor(ICorDebugClass pClass, int nTypeArgs, ICorDebugType[]? ppTypeArgs)
        {
            instance.Eval2.NewParameterizedObjectNoConstructor(pClass, nTypeArgs, ppTypeArgs);
        }

        /// <inheritdoc cref="M:Pitstop.Debugger.Interop.ICorDebugEval2.TryNewStringWithLength(System.String,System.UInt32)" />
        public void NewStringWithLength(string @string)
        {
            instance.Eval2.NewStringWithLength(@string);
        }
    }
}

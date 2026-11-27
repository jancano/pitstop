#nullable enable

namespace Pitstop.Debugger.Interop;

/// <summary>
/// <para>Specifies the state of a thread for debugging.</para>
/// </summary>
/// <remarks>
/// <para>The debugger uses the <c>CorDebugThreadState</c> enumeration to control a thread's execution. The state of a thread can be set by using the <see cref="M:Pitstop.Debugger.Interop.ICorDebugThread.TrySetDebugState(Pitstop.Debugger.Interop.CorDebugThreadState)">ICorDebugThread::SetDebugState</see> or <see cref="M:Pitstop.Debugger.Interop.ICorDebugController.TrySetAllThreadsDebugState(Pitstop.Debugger.Interop.CorDebugThreadState,Pitstop.Debugger.Interop.ICorDebugThread)">ICorDebugController::SetAllThreadsDebugState</see> method.</para>
/// <para>A callback provided to the <see href="https://learn.microsoft.com/dotnet/framework/unmanaged-api/hosting/index">hosting API</see> enables message pumping, so an interrupted state is not needed.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugthreadstate-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugThreadState
{
    /// <summary>
    /// <para>The thread runs freely, unless a debug event occurs.</para>
    /// </summary>
    THREAD_RUN,
    /// <summary>
    /// <para>The thread cannot run.</para>
    /// </summary>
    THREAD_SUSPEND
}

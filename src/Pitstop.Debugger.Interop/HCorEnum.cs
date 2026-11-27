#nullable enable

namespace Pitstop.Debugger.Interop;

public struct HCorEnum
{
    private nint hEnum;

    public readonly bool IsNull => hEnum == 0;
}

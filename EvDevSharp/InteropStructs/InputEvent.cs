using System.Runtime.InteropServices;

namespace EvDevSharp.InteropStructs;

[StructLayout(LayoutKind.Sequential)]
internal struct InputEvent
{
    public TimeVal time;
    public ushort type;
    public ushort code;
    public int value;
}
using System.Runtime.InteropServices;

namespace EvDevSharp.Example;

internal static class UnixEnvironment
{
    [DllImport("libc")]
    private static extern uint getuid();

    [DllImport("libc")]
    private static extern uint geteuid();

    public const long RootUserId = 0;

    public static uint GetUserId()
    {
        return getuid();
    }

    public static uint GetEffectiveUserId()
    {
        return geteuid();
    }
}
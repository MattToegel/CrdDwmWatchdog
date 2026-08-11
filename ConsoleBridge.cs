using System.Runtime.InteropServices;

namespace CrdDwmWatchdog;

internal static class ConsoleBridge
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static void AttachToParent()
    {
        if (!AttachConsole(AttachParentProcess))
        {
            return;
        }

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}

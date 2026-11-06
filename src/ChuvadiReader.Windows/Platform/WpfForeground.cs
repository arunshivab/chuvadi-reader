using System.Runtime.InteropServices;

namespace ChuvadiReader.Windows.Platform;

/// <summary>
/// A launch that hands its files to the running copy owns the foreground (the person just opened
/// it), so it lets any process take the foreground; the running copy can then come to the front.
/// Without this, Windows would only flash its taskbar button.
/// </summary>
internal static class WpfForeground
{
    private const int AsfwAny = -1;

    public static void AllowAnyProcess() => AllowSetForegroundWindow(AsfwAny);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}

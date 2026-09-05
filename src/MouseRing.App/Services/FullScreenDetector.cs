using System.Runtime.InteropServices;
using MouseRing.Interop;

namespace MouseRing.Services;

public static class FullScreenDetector
{
    private const int EdgeTolerance = 2;

    public static bool IsForegroundFullScreen()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == nint.Zero || !NativeMethods.GetWindowRect(window, out var windowRect))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFO
        {
            Size = Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        return windowRect.Left <= info.Monitor.Left + EdgeTolerance &&
            windowRect.Top <= info.Monitor.Top + EdgeTolerance &&
            windowRect.Right >= info.Monitor.Right - EdgeTolerance &&
            windowRect.Bottom >= info.Monitor.Bottom - EdgeTolerance;
    }
}

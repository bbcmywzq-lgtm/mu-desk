using System.Runtime.InteropServices;

namespace DesktopOrganizer.Services;

public sealed class DesktopHostService
{
    private const uint SpawnWorkerMessage = 0x052C;
    private const int GwlStyle = -16;
    private const int GwlOwner = -8;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((long)0x80000000L);
    private const uint SmtoNormal = 0x0000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private static readonly IntPtr HwndBottom = new(1);

    private IntPtr _desktopHost;
    private IntPtr _originalOwner;
    private long _originalStyle;
    private bool _stateCaptured;

    public bool Attach(IntPtr windowHandle)
    {
        var host = FindDesktopHost();
        if (host == IntPtr.Zero)
        {
            SizeAsFallbackWindow(windowHandle);
            return false;
        }

        if (!_stateCaptured)
        {
            _originalOwner = GetWindowLongPtr(windowHandle, GwlOwner);
            _originalStyle = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
            _stateCaptured = true;
        }

        _desktopHost = host;
        // A transparent WPF window may stop rendering when made a cross-process
        // child of Progman/WorkerW. Keep it top-level and make the desktop icon
        // layer its owner instead. HWND_BOTTOM then keeps it above the desktop
        // owner but below ordinary application windows.
        SetParent(windowHandle, IntPtr.Zero);
        var desktopStyle = (_originalStyle & ~WsChild) | WsPopup;
        SetWindowLongPtr(windowHandle, GwlStyle, new IntPtr(desktopStyle));
        SetWindowLongPtr(windowHandle, GwlOwner, host);
        SizeToHost(windowHandle, host);
        return true;
    }

    public void EnsureAttached(IntPtr windowHandle)
    {
        if (_desktopHost == IntPtr.Zero ||
            !IsWindow(_desktopHost) ||
            GetWindowLongPtr(windowHandle, GwlOwner) != _desktopHost)
        {
            Attach(windowHandle);
            return;
        }

        SizeToHost(windowHandle, _desktopHost);
    }

    public void Detach(IntPtr windowHandle)
    {
        if (!IsWindow(windowHandle))
        {
            return;
        }

        SetWindowLongPtr(windowHandle, GwlOwner, _originalOwner);
        if (_originalStyle != 0)
        {
            SetWindowLongPtr(windowHandle, GwlStyle, new IntPtr(_originalStyle));
        }

        _desktopHost = IntPtr.Zero;
    }

    private static IntPtr FindDesktopHost()
    {
        var programManager = FindWindow("Progman", null);
        if (programManager == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        SendMessageTimeout(programManager, SpawnWorkerMessage, IntPtr.Zero, IntPtr.Zero, SmtoNormal, 1000, out _);

        var host = IntPtr.Zero;
        EnumWindows((topLevelWindow, _) =>
        {
            var shellView = FindWindowEx(topLevelWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView == IntPtr.Zero)
            {
                return true;
            }

            host = topLevelWindow;
            return false;
        }, IntPtr.Zero);

        return host != IntPtr.Zero ? host : programManager;
    }

    private static void SizeToHost(IntPtr windowHandle, IntPtr host)
    {
        // Use the monitor work area instead of the full desktop host rectangle.
        // This keeps the organizer behind and outside the Windows taskbar,
        // regardless of which screen edge the taskbar occupies.
        if (!TryGetWorkArea(host, out var bounds) && !GetWindowRect(host, out bounds))
        {
            return;
        }

        SetWindowPos(
            windowHandle,
            HwndBottom,
            bounds.Left,
            bounds.Top,
            Math.Max(1, bounds.Right - bounds.Left),
            Math.Max(1, bounds.Bottom - bounds.Top),
            SwpNoActivate | SwpShowWindow | SwpFrameChanged);
    }

    private static void SizeAsFallbackWindow(IntPtr windowHandle)
    {
        if (TryGetWorkArea(windowHandle, out var bounds))
        {
            SetWindowPos(
                windowHandle,
                HwndBottom,
                bounds.Left,
                bounds.Top,
                Math.Max(1, bounds.Right - bounds.Left),
                Math.Max(1, bounds.Bottom - bounds.Top),
                SwpNoActivate | SwpShowWindow);
            return;
        }

        var workArea = System.Windows.SystemParameters.WorkArea;
        SetWindowPos(
            windowHandle,
            HwndBottom,
            (int)workArea.Left,
            (int)workArea.Top,
            Math.Max(1, (int)workArea.Width),
            Math.Max(1, (int)workArea.Height),
            SwpNoActivate | SwpShowWindow);
    }

    private static bool TryGetWorkArea(IntPtr windowHandle, out Rect bounds)
    {
        bounds = default;
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        bounds = info.WorkArea;
        return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect MonitorArea;
        public Rect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out Rect bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LightPet.App.Services;

internal static partial class NativeWindowStyles
{
    private const int ExtendedStyleIndex = -20;
    private const long TransparentStyle = 0x00000020L;

    public static void SetClickThrough(Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        style = enabled ? style | TransparentStyle : style & ~TransparentStyle;
        _ = SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(style));
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);
}

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PersonalToolbox.Native;

internal static class FileShelfWindowInterop
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    public static void MarkAsToolWindow(System.Windows.Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var styles = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(styles | WsExToolWindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newLong);
}

using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace PersonalToolbox.Native;

internal static class CaptureInterop
{
    public const uint WindowDisplayAffinityExcludeFromCapture = 0x00000011;
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const uint D3D11SdkVersion = 7;
    private const uint MonitorDefaultToNearest = 2;

    private static readonly Guid DxgiDeviceIid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid GraphicsCaptureItemInteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    public static IDirect3DDevice CreateDirect3DDevice()
    {
        var result = D3D11CreateDevice(
            IntPtr.Zero,
            driverType: 1,
            IntPtr.Zero,
            D3D11CreateDeviceBgraSupport,
            null,
            0,
            D3D11SdkVersion,
            out var device,
            out _,
            out var context);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            var iid = DxgiDeviceIid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
                try
                {
                    return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                Marshal.Release(context);
            }

            if (device != IntPtr.Zero)
            {
                Marshal.Release(device);
            }
        }
    }

    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor)
    {
        using var factory = WinRT.ActivationFactory.Get(
            "Windows.Graphics.Capture.GraphicsCaptureItem",
            GraphicsCaptureItemInteropIid);
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
        try
        {
            var iid = GraphicsCaptureItemIid;
            Marshal.ThrowExceptionForHR(interop.CreateForMonitor(monitor, ref iid, out var itemPointer));
            try
            {
                return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
            }
            finally
            {
                Marshal.Release(itemPointer);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(interop);
        }
    }

    public static IntPtr MonitorFromPhysicalPoint(int x, int y) =>
        MonitorFromPoint(new NativePoint(x, y), MonitorDefaultToNearest);

    public static bool ExcludeWindowFromCapture(IntPtr windowHandle) =>
        windowHandle != IntPtr.Zero &&
        SetWindowDisplayAffinity(windowHandle, WindowDisplayAffinityExcludeFromCapture);

    public static void MakeWindowClickThrough(IntPtr windowHandle)
        => SetWindowClickThrough(windowHandle, true);

    public static void SetWindowClickThrough(IntPtr windowHandle, bool enabled)
    {
        const int extendedStyleIndex = -20;
        const long transparent = 0x20;
        const long noActivate = 0x08000000;
        var current = GetWindowLongPtr(windowHandle, extendedStyleIndex).ToInt64();
        var mask = transparent | noActivate;
        SetWindowLongPtr(windowHandle, extendedStyleIndex, new IntPtr(enabled ? current | mask : current & ~mask));
    }

    public static byte[] CopyBitmapPixels(Windows.Graphics.Imaging.SoftwareBitmap bitmap)
    {
        using var buffer = bitmap.LockBuffer(Windows.Graphics.Imaging.BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        using var marshaler = WinRT.MarshalInspectable<IMemoryBufferReference>.CreateMarshaler(reference);
        var unknown = WinRT.MarshalInspectable<IMemoryBufferReference>.GetAbi(marshaler);
        var iid = typeof(IMemoryBufferByteAccess).GUID;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var accessPointer));
        var access = (IMemoryBufferByteAccess)Marshal.GetObjectForIUnknown(accessPointer);
        try
        {
            access.GetBuffer(out var data, out _);
            var plane = buffer.GetPlaneDescription(0);
            var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
            var rowBytes = checked(bitmap.PixelWidth * 4);
            for (var row = 0; row < bitmap.PixelHeight; row++)
            {
                Marshal.Copy(
                    IntPtr.Add(data, plane.StartIndex + (row * plane.Stride)),
                    pixels,
                    row * rowBytes,
                    rowBytes);
            }

            return pixels;
        }
        finally
        {
            Marshal.FinalReleaseComObject(access);
            Marshal.Release(accessPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        int[]? featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr windowHandle, uint affinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);
}

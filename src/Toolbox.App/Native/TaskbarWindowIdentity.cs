using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PersonalToolbox.Native;

internal static class TaskbarWindowIdentity
{
    public const string CispQuestionBank = "MU.Desk.CispQuestionBank";
    public const string CursorGallery = "MU.Desk.CursorGallery";
    public const string EffectCapture = "MU.Desk.EffectCapture";

    private static readonly PropertyKey AppUserModelIdKey = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        5);

    public static void Apply(Window window, string appUserModelId)
    {
        window.SourceInitialized += (_, _) => Apply(new WindowInteropHelper(window).Handle, appUserModelId);
    }

    private static void Apply(IntPtr windowHandle, string appUserModelId)
    {
        IPropertyStore? propertyStore = null;
        try
        {
            var interfaceId = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(windowHandle, ref interfaceId, out propertyStore) < 0)
            {
                return;
            }

            using var value = PropVariant.FromString(appUserModelId);
            var key = AppUserModelIdKey;
            if (propertyStore.SetValue(ref key, ref value.Value) >= 0)
            {
                propertyStore.Commit();
            }
        }
        catch (COMException)
        {
            // A missing shell property store should never prevent the module window from opening.
        }
        finally
        {
            if (propertyStore is not null)
            {
                Marshal.ReleaseComObject(propertyStore);
            }
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr windowHandle,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref NativePropVariant value);

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);

        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out NativePropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref NativePropVariant value);

        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativePropVariant
    {
        [FieldOffset(0)]
        public ushort VariantType;

        [FieldOffset(8)]
        public IntPtr PointerValue;
    }

    private sealed class PropVariant : IDisposable
    {
        private const ushort StringVariantType = 31;
        private bool _disposed;

        private PropVariant(string value)
        {
            Value = new NativePropVariant
            {
                VariantType = StringVariantType,
                PointerValue = Marshal.StringToCoTaskMemUni(value),
            };
        }

        public NativePropVariant Value;

        public static PropVariant FromString(string value) => new(value);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            PropVariantClear(ref Value);
            _disposed = true;
        }
    }
}

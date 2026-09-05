using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PersonalToolbox.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const int HotkeyId = 0x4D55;
    private readonly HwndSource _source;
    private readonly Action _action;
    private bool _registered;

    public GlobalHotkeyService(Action action)
    {
        _action = action;
        _source = new HwndSource(new HwndSourceParameters("MU Desk Hotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000),
        });
        _source.AddHook(WindowHook);
    }

    public bool TrySet(string? hotkey)
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return true;
        }

        var (modifiers, key) = hotkey switch
        {
            "Ctrl+Alt+R" => (0x0002u | 0x0001u | 0x4000u, 0x52u),
            "Ctrl+Shift+R" => (0x0002u | 0x0004u | 0x4000u, 0x52u),
            _ => (0u, 0u),
        };
        if (key == 0)
        {
            return false;
        }

        _registered = RegisterHotKey(_source.Handle, HotkeyId, modifiers, key);
        return _registered;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
        }

        _source.RemoveHook(WindowHook);
        _source.Dispose();
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HotkeyMessage && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _action();
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}

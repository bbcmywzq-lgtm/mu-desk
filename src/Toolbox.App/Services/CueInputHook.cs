using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using PersonalToolbox.Native;

namespace PersonalToolbox.Services;

internal sealed class CueInputHook : IDisposable
{
    private readonly Func<int> _holdThreshold;
    private readonly Func<bool> _canTrigger;
    private readonly DispatcherTimer _holdTimer;
    private readonly CueNative.HookProc _keyboardCallback;
    private readonly CueNative.HookProc _mouseCallback;
    private readonly Queue<long> _escapeTimes = new();
    private nint _keyboardHook;
    private nint _mouseHook;
    private bool _capsDown;
    private bool _focusActive;

    public CueInputHook(Func<int> holdThreshold, Func<bool> canTrigger)
    {
        _holdThreshold = holdThreshold;
        _canTrigger = canTrigger;
        _keyboardCallback = KeyboardCallback;
        _mouseCallback = MouseCallback;
        _holdTimer = new DispatcherTimer(DispatcherPriority.Input);
        _holdTimer.Tick += (_, _) => BeginFocusAfterHold();
    }

    public event Action? FocusStarted;

    public event Action? FocusEnded;

    public event Action<int>? FocusZoomRequested;

    public event Action<System.Drawing.Point>? PointerMoved;

    public event Action<System.Drawing.Point>? Clicked;

    public event Action? EmergencyReset;

    public void Start()
    {
        if (_keyboardHook != nint.Zero)
        {
            return;
        }

        var module = CueNative.GetModuleHandle(null);
        _keyboardHook = CueNative.SetWindowsHookEx(CueNative.WhKeyboardLl, _keyboardCallback, module, 0);
        if (_keyboardHook == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装 Mujun Cue 键盘监听。");
        }

        _mouseHook = CueNative.SetWindowsHookEx(CueNative.WhMouseLl, _mouseCallback, module, 0);
        if (_mouseHook == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            CueNative.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = nint.Zero;
            throw new Win32Exception(error, "无法安装 Mujun Cue 鼠标监听。");
        }
    }

    public void CancelActiveInput()
    {
        _holdTimer.Stop();
        _capsDown = false;
        if (_focusActive)
        {
            _focusActive = false;
            FocusEnded?.Invoke();
        }
    }

    public void Dispose()
    {
        CancelActiveInput();
        if (_mouseHook != nint.Zero)
        {
            CueNative.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = nint.Zero;
        }

        if (_keyboardHook != nint.Zero)
        {
            CueNative.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = nint.Zero;
        }
    }

    private nint KeyboardCallback(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code < 0)
            {
                return CueNative.CallNextHookEx(_keyboardHook, code, wParam, lParam);
            }

            var data = Marshal.PtrToStructure<CueNative.KeyboardHookData>(lParam);
            if ((data.Flags & CueNative.LlkhfInjected) != 0)
            {
                return CueNative.CallNextHookEx(_keyboardHook, code, wParam, lParam);
            }

            var message = unchecked((int)wParam);
            var down = message is CueNative.WmKeyDown or CueNative.WmSysKeyDown;
            var up = message is CueNative.WmKeyUp or CueNative.WmSysKeyUp;

            if (down && data.VkCode == CueNative.VkEscape)
            {
                ObserveEscape();
            }

            if (down && data.VkCode == CueNative.VkEscape &&
                IsKeyDown(0x11) && IsKeyDown(0x12))
            {
                EmergencyReset?.Invoke();
                return 1;
            }

            if (data.VkCode != CueNative.VkCapsLock || !_canTrigger())
            {
                return CueNative.CallNextHookEx(_keyboardHook, code, wParam, lParam);
            }

            if (down)
            {
                if (!_capsDown)
                {
                    _capsDown = true;
                    _holdTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_holdThreshold(), 80, 500));
                    _holdTimer.Start();
                }

                return 1;
            }

            if (up && _capsDown)
            {
                _holdTimer.Stop();
                _capsDown = false;
                if (_focusActive)
                {
                    _focusActive = false;
                    FocusEnded?.Invoke();
                }
                else
                {
                    ForwardCapsLockTap();
                }

                return 1;
            }
        }
        catch (Exception)
        {
            _holdTimer.Stop();
            _capsDown = false;
            if (_focusActive)
            {
                _focusActive = false;
                EmergencyReset?.Invoke();
            }
        }

        return CueNative.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private nint MouseCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return CueNative.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        try
        {
            var data = Marshal.PtrToStructure<CueNative.MouseHookData>(lParam);
            var point = new System.Drawing.Point(data.Point.X, data.Point.Y);
            var message = unchecked((int)wParam);
            if (message == CueNative.WmMouseMove)
            {
                PointerMoved?.Invoke(point);
            }
            else if (message == CueNative.WmLButtonUp)
            {
                Clicked?.Invoke(point);
            }
            else if (message == CueNative.WmMouseWheel && _focusActive)
            {
                var delta = unchecked((short)(data.MouseData >> 16));
                FocusZoomRequested?.Invoke(Math.Sign(delta));
                return 1;
            }
        }
        catch (Exception)
        {
            // Input hooks must never take down the host process.
        }

        return CueNative.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void BeginFocusAfterHold()
    {
        _holdTimer.Stop();
        if (!_capsDown || !_canTrigger())
        {
            return;
        }

        _focusActive = true;
        FocusStarted?.Invoke();
    }

    private void ObserveEscape()
    {
        var now = Stopwatch.GetTimestamp();
        var window = Stopwatch.Frequency * 12 / 10;
        _escapeTimes.Enqueue(now);
        while (_escapeTimes.TryPeek(out var timestamp) && now - timestamp > window)
        {
            _escapeTimes.Dequeue();
        }

        if (_escapeTimes.Count >= 3)
        {
            _escapeTimes.Clear();
            EmergencyReset?.Invoke();
        }
    }

    private static bool IsKeyDown(int key) => (CueNative.GetAsyncKeyState(key) & 0x8000) != 0;

    private static void ForwardCapsLockTap()
    {
        CueNative.keybd_event((byte)CueNative.VkCapsLock, 0, 0, 0);
        CueNative.keybd_event((byte)CueNative.VkCapsLock, 0, CueNative.KeyeventfKeyup, 0);
    }
}

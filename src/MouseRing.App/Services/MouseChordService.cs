using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using MouseRing.Interop;

namespace MouseRing.Services;

internal sealed class MouseChordService : IDisposable
{
    private const double RightDragPassThroughDistance = 8;

    private readonly Func<int> _delayProvider;
    private readonly Func<bool> _isPaused;
    private readonly Func<bool> _isDisabledForForeground;
    private readonly DispatcherTimer _openTimer;
    private readonly NativeMethods.HookProc _mouseCallback;
    private readonly NativeMethods.HookProc _keyboardCallback;
    private nint _mouseHook;
    private nint _keyboardHook;
    private NativeMethods.POINT _anchor;
    private bool _rightCandidate;
    private bool _rightPassThrough;
    private bool _gestureActive;
    private bool _menuOpened;
    private bool _suppressRightUp;
    private bool _suppressNextLeftUp;

    public MouseChordService(
        Func<int> delayProvider,
        Func<bool> isPaused,
        Func<bool> isDisabledForForeground)
    {
        _delayProvider = delayProvider;
        _isPaused = isPaused;
        _isDisabledForForeground = isDisabledForForeground;
        _mouseCallback = MouseHookCallback;
        _keyboardCallback = KeyboardHookCallback;
        _openTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(80, delayProvider())),
        };
        _openTimer.Tick += OnOpenTimerTick;
    }

    public event Action<NativeMethods.POINT>? MenuOpened;

    public event Action<NativeMethods.POINT>? PointerMoved;

    public event Action? GestureCompleted;

    public event Action? GestureCancelled;

    public void Start()
    {
        var module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _mouseCallback, module, 0);
        if (_mouseHook == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装全局鼠标监听。");
        }

        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardCallback, module, 0);
        if (_keyboardHook == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = nint.Zero;
            throw new Win32Exception(error, "无法安装键盘取消监听。");
        }
    }

    public void Dispose()
    {
        _openTimer.Stop();
        if (_keyboardHook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = nint.Zero;
        }

        if (_mouseHook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = nint.Zero;
        }
    }

    private nint MouseHookCallback(int code, nint wParam, nint lParam)
    {
        try
        {
            return MouseHookCallbackCore(code, wParam, lParam);
        }
        catch (Exception)
        {
            _openTimer.Stop();
            _suppressRightUp = _gestureActive || _rightCandidate;
            ResetGestureState();
            GestureCancelled?.Invoke();
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }
    }

    private nint MouseHookCallbackCore(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        if ((data.Flags & NativeMethods.LlMhfInjected) != 0)
        {
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        var message = unchecked((int)wParam);
        switch (message)
        {
            case NativeMethods.WmRButtonDown:
                if (_isPaused() || _isDisabledForForeground())
                {
                    return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
                }

                _anchor = data.Point;
                _rightCandidate = true;
                _rightPassThrough = false;
                return 1;

            case NativeMethods.WmMouseMove:
                if (_gestureActive)
                {
                    if (_menuOpened)
                    {
                        PointerMoved?.Invoke(data.Point);
                    }

                    return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
                }

                if (_rightCandidate && Distance(_anchor, data.Point) >= RightDragPassThroughDistance)
                {
                    _rightCandidate = false;
                    _rightPassThrough = true;
                    InputSender.SendRightButtonDown();
                }

                return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);

            case NativeMethods.WmLButtonDown:
                if (_rightCandidate)
                {
                    _rightCandidate = false;
                    _gestureActive = true;
                    _menuOpened = false;
                    _suppressNextLeftUp = true;
                    _openTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(80, _delayProvider()));
                    _openTimer.Start();
                    return 1;
                }

                break;

            case NativeMethods.WmLButtonUp:
                if (_suppressNextLeftUp)
                {
                    _suppressNextLeftUp = false;
                    return 1;
                }

                break;

            case NativeMethods.WmRButtonUp:
                if (_gestureActive)
                {
                    _openTimer.Stop();
                    if (_menuOpened)
                    {
                        GestureCompleted?.Invoke();
                    }
                    else
                    {
                        GestureCancelled?.Invoke();
                    }

                    ResetGestureState();
                    return 1;
                }

                if (_suppressRightUp)
                {
                    _suppressRightUp = false;
                    return 1;
                }

                if (_rightCandidate)
                {
                    _rightCandidate = false;
                    InputSender.SendRightClick();
                    return 1;
                }

                if (_rightPassThrough)
                {
                    _rightPassThrough = false;
                }

                break;
        }

        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private nint KeyboardHookCallback(int code, nint wParam, nint lParam)
    {
        try
        {
            return KeyboardHookCallbackCore(code, wParam, lParam);
        }
        catch (Exception)
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        }
    }

    private nint KeyboardHookCallbackCore(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _gestureActive &&
            (unchecked((int)wParam) == NativeMethods.WmKeyDown ||
             unchecked((int)wParam) == NativeMethods.WmSysKeyDown))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (data.VkCode == NativeMethods.VkEscape)
            {
                _openTimer.Stop();
                _suppressRightUp = true;
                ResetGestureState();
                GestureCancelled?.Invoke();
                return 1;
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void OnOpenTimerTick(object? sender, EventArgs e)
    {
        _openTimer.Stop();
        if (!_gestureActive || _isPaused() || _isDisabledForForeground())
        {
            _suppressRightUp = _gestureActive;
            ResetGestureState();
            GestureCancelled?.Invoke();
            return;
        }

        _menuOpened = true;
        MenuOpened?.Invoke(_anchor);
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            PointerMoved?.Invoke(cursor);
        }
    }

    private void ResetGestureState()
    {
        _rightCandidate = false;
        _rightPassThrough = false;
        _gestureActive = false;
        _menuOpened = false;
    }

    private static double Distance(NativeMethods.POINT first, NativeMethods.POINT second)
    {
        var x = (double)second.X - first.X;
        var y = (double)second.Y - first.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}

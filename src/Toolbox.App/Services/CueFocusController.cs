using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PersonalToolbox.Native;
using Toolbox.Core;
using Forms = System.Windows.Forms;

namespace PersonalToolbox.Services;

internal sealed class CueFocusController : IDisposable
{
    private readonly CueSettings _settings;
    private readonly Action _settingsChanged;
    private readonly CueCameraMotion _camera = new();
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _resetDone = new(true);
    private readonly Thread _thread;
    private bool _active, _returning, _entering, _resetRequested, _disposed;
    private System.Drawing.Rectangle _bounds;
    public event Action<double>? ZoomChanged;
    public event Action<Exception>? Error;

    public CueFocusController(CueSettings settings, Action settingsChanged)
    {
        _settings = settings;
        _settingsChanged = settingsChanged;
        _thread = new Thread(Run) { IsBackground = true, Name = "Cue native camera" };
        _thread.Start();
    }
    public bool IsActive { get { lock (_gate) return _active || _returning; } }
    public void Begin()
    {
        var point = PhysicalCursor();
        lock (_gate)
        {
            _bounds = Forms.Screen.FromPoint(point).Bounds;
            _active = true; _returning = false; _entering = true;
            // Preserve current position AND velocity when interrupted.
            _camera.AimAt(_settings.ZoomFactor, point.X, point.Y);
        }
        _wake.Set();
    }
    public void End()
    {
        lock (_gate)
        {
            if (!_active) return;
            _active = false; _returning = true;
            _camera.Aim(1, 0, 0);
        }
        _wake.Set();
    }
    public void AdjustZoom(int direction)
    {
        lock (_gate)
        {
            if (!_active || direction == 0) return;
            _settings.ZoomFactor = Math.Clamp(_settings.ZoomFactor + Math.Sign(direction) * .25, 1.25, 5);
            var point = PhysicalCursor();
            _camera.AimAt(_settings.ZoomFactor, point.X, point.Y);
            _entering = true;
        }
        _settingsChanged();
    }
    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _resetDone.Reset();
            _active = false; _returning = false; _resetRequested = true;
        }
        _wake.Set();
        if (Thread.CurrentThread != _thread) _resetDone.Wait(TimeSpan.FromSeconds(2));
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _wake.Set();
        if (Thread.CurrentThread != _thread) _thread.Join();
        _wake.Dispose(); _resetDone.Dispose();
    }
    private void Run()
    {
        var initialized = false;
        var previous = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                bool moving;
                lock (_gate)
                {
                    if (_disposed) break;
                    moving = _active || _returning || _resetRequested;
                }
                if (!moving) { _wake.WaitOne(); previous = Stopwatch.GetTimestamp(); continue; }
                try
                {
                    if (!initialized)
                    {
                        if (!CueNative.MagInitialize()) throw new Win32Exception(Marshal.GetLastWin32Error());
                        initialized = true;
                        // Initialization latency must not count as animation time:
                        // otherwise the first visible sample can skip the entrance.
                        previous = Stopwatch.GetTimestamp();
                    }
                    var now = Stopwatch.GetTimestamp();
                    var dt = Stopwatch.GetElapsedTime(previous, now).TotalSeconds;
                    previous = now;
                    double zoom;
                    lock (_gate)
                    {
                        if (_resetRequested)
                        {
                            Apply(1, 0, 0); _camera.Reset(); _resetRequested = false;
                            _resetDone.Set();
                        }
                        else
                        {
                            if (_active && !_entering && _settings.FollowCursor) Follow();
                            _camera.Step(dt, _settings.AnimationMilliseconds);
                            Apply(_camera.Zoom, _camera.X, _camera.Y);
                            if (_entering && _camera.IsSettled) _entering = false;
                            if (_returning && _camera.IsSettled)
                            {
                                Apply(1, 0, 0); _camera.Reset(); _returning = false;
                            }
                        }
                        zoom = _camera.Zoom;
                    }
                    ZoomChanged?.Invoke(zoom);
                    // UI work cannot block this thread. DWM provides pacing.
                    if (DwmFlush() < 0) _wake.WaitOne(8);
                }
                catch (Exception exception)
                {
                    lock (_gate)
                    {
                        CueNative.MagSetFullscreenTransform(1, 0, 0);
                        _camera.Reset(); _active = false; _returning = false; _resetRequested = false;
                        _resetDone.Set();
                    }
                    Error?.Invoke(exception);
                }
            }
        }
        finally
        {
            if (initialized) { CueNative.MagSetFullscreenTransform(1, 0, 0); CueNative.MagUninitialize(); }
            _resetDone.Set();
        }
    }
    private void Follow()
    {
        var cursor = PhysicalCursor();
        var safeX = Math.Clamp(cursor.X, _bounds.Left + _bounds.Width * .2, _bounds.Right - _bounds.Width * .2);
        var safeY = Math.Clamp(cursor.Y, _bounds.Top + _bounds.Height * .2, _bounds.Bottom - _bounds.Height * .2);
        var x = _camera.X + safeX - cursor.X;
        var y = _camera.Y + safeY - cursor.Y;
        // Clamp the destination, never the animated intermediate position.
        var z = _camera.TargetZoom;
        x = Math.Clamp(x, _bounds.Right * (1 - z), _bounds.Left * (1 - z));
        y = Math.Clamp(y, _bounds.Bottom * (1 - z), _bounds.Top * (1 - z));
        if (safeX != cursor.X || safeY != cursor.Y) _camera.Aim(z, x, y);
    }
    private static void Apply(double zoom, double x, double y)
    {
        zoom = Math.Max(1, zoom);
        if (!CueNative.MagSetFullscreenTransform((float)zoom, (int)Math.Round(-x / zoom), (int)Math.Round(-y / zoom)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 无法应用聚焦变换。");
    }
    private static System.Drawing.Point PhysicalCursor() =>
        GetPhysicalCursorPos(out var point) ? point : Forms.Cursor.Position;
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out System.Drawing.Point point);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}

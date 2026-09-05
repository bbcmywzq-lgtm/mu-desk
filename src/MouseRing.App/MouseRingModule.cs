using System.ComponentModel;
using System.Windows;
using MouseRing.Core;
using MouseRing.Interop;
using MouseRing.Services;
using MouseRing.Views;
using Toolbox.Core;

namespace MouseRing;

public sealed class MouseRingModule : IToolModule
{
    private readonly SettingsStore _settingsStore = new();
    private readonly bool _isHosted;
    private readonly Action? _effectCapture;
    private Mutex? _runtimeMutex;
    private bool _ownsRuntimeMutex;
    private RadialMenuWindow? _menuWindow;
    private MouseChordService? _mouseChord;
    private ActionExecutor? _actionExecutor;
    private bool _paused;

    public MouseRingModule(bool isHosted = true, Action? effectCapture = null)
    {
        _isHosted = isHosted;
        _effectCapture = effectCapture;
        Settings = _settingsStore.Load();
    }

    public string Id => "mouse-ring";

    public string DisplayName => "Mujun Orbit";

    public string Description => "按住右键并点一下左键，呼出四向快捷菜单。";

    public bool IsRunning { get; private set; }

    public bool IsPaused => _paused;

    public AppSettings Settings { get; private set; }

    public event EventHandler? StateChanged;

    public event EventHandler<ModuleErrorEventArgs>? Error;

    public bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        if (!AcquireRuntimeOwnership())
        {
            RaiseError("Orbit 独立版正在运行，工具箱无法重复加载该模块。");
            return false;
        }

        try
        {
            _menuWindow = new RadialMenuWindow();
            _actionExecutor = new ActionExecutor(_effectCapture);
            _mouseChord = new MouseChordService(
                () => Settings.TriggerDelayMs,
                () => _paused,
                () => Settings.DisableInFullScreen && FullScreenDetector.IsForegroundFullScreen());
            _mouseChord.MenuOpened += OnMenuOpened;
            _mouseChord.PointerMoved += point => _menuWindow?.UpdatePointer(point);
            _mouseChord.GestureCompleted += OnGestureCompleted;
            _mouseChord.GestureCancelled += () => _menuWindow?.HideMenu();
            _mouseChord.Start();
            IsRunning = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Win32Exception exception)
        {
            RaiseError(exception.Message);
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        _mouseChord?.Dispose();
        _mouseChord = null;
        _menuWindow?.Close();
        _menuWindow = null;
        _actionExecutor?.Dispose();
        _actionExecutor = null;
        _paused = false;

        if (_ownsRuntimeMutex)
        {
            _runtimeMutex?.ReleaseMutex();
            _ownsRuntimeMutex = false;
        }

        _runtimeMutex?.Dispose();
        _runtimeMutex = null;
        var wasRunning = IsRunning;
        IsRunning = false;
        if (wasRunning)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        if (paused)
        {
            _menuWindow?.HideMenu();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool OpenSettings()
    {
        var editable = CloneSettings(Settings);
        var window = new SettingsWindow(editable, isHosted: _isHosted);
        if (window.ShowDialog() != true)
        {
            return false;
        }

        Settings = window.Result;
        if (!_settingsStore.Save(Settings))
        {
            RaiseError("Orbit 设置无法写入本地配置文件。");
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ShowPreview()
    {
        if (!IsRunning || _menuWindow is null || !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        _menuWindow.EnableInteractivePreview();
        OnMenuOpened(cursor);
        _menuWindow.UpdatePointer(new NativeMethods.POINT(cursor.X + 90, cursor.Y));
        _menuWindow.Activate();
    }

    public void Dispose() => Stop();

    private void OnMenuOpened(NativeMethods.POINT anchor)
    {
        var actions = new Dictionary<Direction, ActionKind>
        {
            [Direction.Up] = Settings.UpAction,
            [Direction.Right] = Settings.RightAction,
            [Direction.Down] = Settings.DownAction,
            [Direction.Left] = Settings.LeftAction,
        };
        _menuWindow?.ShowAt(anchor, Settings.MenuDiameter, actions);
    }

    private void OnGestureCompleted()
    {
        if (_menuWindow is null)
        {
            return;
        }

        var direction = _menuWindow.SelectedDirection;
        _menuWindow.HideMenu();
        var action = Settings.GetAction(direction);
        if (action == ActionKind.None)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_actionExecutor?.Execute(action) == false)
            {
                RaiseError($"无法执行“{ActionCatalog.Get(action).Label}”。");
            }
        });
    }

    private bool AcquireRuntimeOwnership()
    {
        _runtimeMutex = new Mutex(false, "Local\\MouseRing.Runtime");
        try
        {
            _ownsRuntimeMutex = _runtimeMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsRuntimeMutex = true;
        }

        return _ownsRuntimeMutex;
    }

    private void RaiseError(string message) => Error?.Invoke(this, new ModuleErrorEventArgs(message));

    private static AppSettings CloneSettings(AppSettings source) => new()
    {
        VisualRevision = source.VisualRevision,
        TriggerDelayMs = source.TriggerDelayMs,
        MenuDiameter = source.MenuDiameter,
        DisableInFullScreen = source.DisableInFullScreen,
        RunAtStartup = source.RunAtStartup,
        UpAction = source.UpAction,
        RightAction = source.RightAction,
        DownAction = source.DownAction,
        LeftAction = source.LeftAction,
    };
}

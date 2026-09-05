using Toolbox.Core;

namespace DesktopOrganizer;

public sealed class DesktopOrganizerModule : IToolModule
{
    private Mutex? _runtimeMutex;
    private bool _ownsRuntimeMutex;
    private MainWindow? _window;
    private bool _paused;

    public string Id => "desktop-organizer";

    public string DisplayName => "Mujun Grid";

    public string Description => "把桌面文件整理进可视分区，并保持规则与布局。";

    public bool IsRunning { get; private set; }

    public bool IsPaused => _paused;

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
            RaiseError("Grid 兼容版正在运行，MU Desk 无法重复加载该模块。");
            return false;
        }

        try
        {
            _window = new MainWindow(isHosted: true);
            _window.Show();
            IsRunning = true;
            _paused = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            RaiseError($"Grid 启动失败：{exception.Message}");
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        if (_window is not null)
        {
            _window.PrepareForShutdown();
            _window.Close();
            _window = null;
        }

        _paused = false;
        var wasRunning = IsRunning;
        IsRunning = false;
        if (_ownsRuntimeMutex)
        {
            _runtimeMutex?.ReleaseMutex();
            _ownsRuntimeMutex = false;
        }

        _runtimeMutex?.Dispose();
        _runtimeMutex = null;
        if (wasRunning)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetPaused(bool paused)
    {
        if (!IsRunning || _paused == paused || _window is null)
        {
            return;
        }

        _paused = paused;
        if (paused)
        {
            _window.Hide();
        }
        else
        {
            _window.ShowAndActivate();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool OpenSettings()
    {
        if (_window is null)
        {
            return false;
        }

        _window.OpenSettings();
        return true;
    }

    public void ShowDesktop()
    {
        if (_window is null)
        {
            return;
        }

        if (_paused)
        {
            SetPaused(false);
        }

        _window.ShowAndActivate();
    }

    public void ApplyRulesNow() => _window?.ApplyRulesNow();

    public void UndoLastAction() => _window?.UndoLastAction();

    public void Dispose() => Stop();

    private bool AcquireRuntimeOwnership()
    {
        _runtimeMutex = new Mutex(false, "Local\\DesktopOrganizer.Runtime");
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
}

using PersonalToolbox.Views;
using Toolbox.Core;

namespace PersonalToolbox.Modules;

public sealed class CursorGalleryModule : IToolModule
{
    private readonly CursorLibraryService _libraryService = new();
    private CursorGalleryWindow? _window;

    public string Id => "cursor-gallery";

    public string DisplayName => "Mujun Tip";

    public string Description => "浏览并切换本机光标皮肤，不常驻额外进程。";

    public bool IsRunning { get; private set; }

    public bool IsPaused => false;

    public event EventHandler? StateChanged;

    public event EventHandler<ModuleErrorEventArgs>? Error;

    public bool Start()
    {
        IsRunning = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Stop()
    {
        _window?.Close();
        _window = null;
        var wasRunning = IsRunning;
        IsRunning = false;
        if (wasRunning)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetPaused(bool paused)
    {
    }

    public bool OpenSettings()
    {
        try
        {
            if (_window is { IsVisible: true })
            {
                _window.Show();
                if (_window.WindowState == System.Windows.WindowState.Minimized)
                {
                    _window.WindowState = System.Windows.WindowState.Normal;
                }

                _window.Activate();
                _window.Topmost = true;
                _window.Topmost = false;
                _window.Focus();
                return true;
            }

            _window = new CursorGalleryWindow(_libraryService);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            _window.Activate();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.IO.IOException)
        {
            Error?.Invoke(this, new ModuleErrorEventArgs(exception.Message));
            return false;
        }
    }

    public void Dispose() => Stop();
}

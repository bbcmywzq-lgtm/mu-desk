using System.IO;
using System.Windows;
using PersonalToolbox.Services;
using PersonalToolbox.Views;
using Toolbox.Core;
using Forms = System.Windows.Forms;

namespace PersonalToolbox.Modules;

public sealed class CueModule : IToolModule, IDisposable
{
    private readonly CueSettings _settings;
    private readonly Action _saveSettings;
    private CueInputHook? _input;
    private CueFocusController? _focus;
    private CueGpuMagnifier? _magnifier;
    private CueOverlayWindow? _overlay;
    private CueToolbarWindow? _toolbar;
    private CueAnnotationWindow? _annotation;
    private readonly CueAnnotationDocument _annotationDocument = new();

    public CueModule(CueSettings settings, Action saveSettings)
    {
        _settings = settings;
        _saveSettings = saveSettings;
    }

    public string Id => "mujun-cue";

    public string DisplayName => "Mujun Cue";

    public string Description => "聚焦、放大并讲清屏幕内容。";

    public bool IsRunning { get; private set; }

    public bool IsPaused { get; private set; }
    public bool IsAnnotating { get; private set; }
    internal CueAnnotationWindow? Annotation => _annotation;

    public bool IsOverlayActive =>
        _settings.PointerRingEnabled || _settings.LaserEnabled || _settings.ClickPulseEnabled ||
        _settings.SpotlightEnabled;

    public CueSettings Settings => _settings;

    public event EventHandler? StateChanged;

    public event EventHandler<ModuleErrorEventArgs>? Error;

    public bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        try
        {
            _focus = new CueFocusController(_settings, Save);
            _focus.Error += exception => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Error?.Invoke(
                this,
                new ModuleErrorEventArgs($"Mujun Cue 聚焦已请求复原：{exception.Message}")));
            _input = new CueInputHook(
                () => _settings.HoldThresholdMilliseconds,
                () => IsRunning && !IsPaused && !IsAnnotating && _settings.FocusHoldEnabled);
            _input.FocusStarted += BeginFocus;
            _input.FocusEnded += EndFocus;
            _input.FocusZoomRequested += direction => Guard(() => _focus?.AdjustZoom(direction));
            _input.Clicked += point => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _overlay?.AddClick(point));
            _input.EmergencyReset += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(ResetAll);
            _input.Start();
            IsRunning = true;
            IsPaused = false;
            RefreshOverlay();
            EnsureToolbar().CollapseToNotch();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            DisposeRuntime();
            IsRunning = false;
            IsPaused = false;
            Error?.Invoke(this, new ModuleErrorEventArgs($"Mujun Cue 无法启动：{exception.Message}"));
            return false;
        }
    }

    public void Stop()
    {
        DisposeRuntime();
        IsRunning = false;
        IsPaused = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPaused(bool paused)
    {
        if (!IsRunning || IsPaused == paused)
        {
            return;
        }

        IsPaused = paused;
        if (paused)
        {
            _input?.CancelActiveInput();
            _focus?.Reset();
            _overlay?.Hide();
            StopMagnifier();
            ExitAnnotation();
        }
        else
        {
            RefreshOverlay();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool OpenSettings()
    {
        var window = new CueSettingsWindow(_settings, ApplySettings);
        window.Show();
        window.Activate();
        return true;
    }

    public void OpenToolbar()
    {
        if (!IsRunning)
        {
            _settings.Enabled = true;
            if (!Start())
            {
                return;
            }

            Save();
        }

        EnsureToolbar().Expand();
    }

    private CueToolbarWindow EnsureToolbar()
    {
        if (_toolbar is null)
        {
            _toolbar = new CueToolbarWindow(this);
            _toolbar.Closed += (_, _) => _toolbar = null;
        }
        return _toolbar;
    }

    public void TogglePointerRing() => Toggle(() => _settings.PointerRingEnabled = !_settings.PointerRingEnabled);

    public void ToggleLaser() => Toggle(() => _settings.LaserEnabled = !_settings.LaserEnabled);

    public void ToggleClickPulse() => Toggle(() => _settings.ClickPulseEnabled = !_settings.ClickPulseEnabled);

    public void ToggleSpotlight() => Toggle(() => _settings.SpotlightEnabled = !_settings.SpotlightEnabled);

    public void ToggleMagnifier() => Toggle(() => _settings.MagnifierEnabled = !_settings.MagnifierEnabled);

    public void ToggleAnnotation()
    {
        if (IsAnnotating) { ExitAnnotation(); return; }
        Guard(() =>
        {
            _input?.CancelActiveInput(); _focus?.Reset(); StopMagnifier(); _overlay?.Hide();
            if (_annotation is null)
            {
                _annotation = new CueAnnotationWindow(_annotationDocument);
                _annotation.ExitRequested += ExitAnnotation;
                _annotation.AnnotationStateChanged += () => _toolbar?.Refresh();
            }
            IsAnnotating = true;
            var toolbar = EnsureToolbar();
            _annotation.ToolWindow = toolbar;
            _annotation.Resume();
            toolbar.Owner = _annotation;
            toolbar.Expand();
        });
    }

    public void ExitAnnotation()
    {
        if (!IsAnnotating) return;
        IsAnnotating = false;
        if (_toolbar is not null) _toolbar.Owner = null;
        _annotation?.Suspend();
        _toolbar?.Expand();
        RefreshOverlay();
    }

    public void CaptureCurrentMonitor()
    {
        Guard(() => CaptureRectangle(Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds));
    }

    public void CaptureRegion()
    {
        Guard(() =>
        {
            var toolbarWasVisible = _toolbar?.IsVisible == true;
            TemporarilyHideOverlays();
            try
            {
                var picker = new CueRegionSelectionWindow();
                if (picker.ShowDialog() == true && picker.SelectedRegion is { } region)
                {
                    CaptureRectangle(region);
                }
            }
            finally
            {
                RefreshOverlay();
                if (toolbarWasVisible) _toolbar?.Show();
            }
        });
    }

    public void ResetAll()
    {
        _input?.CancelActiveInput();
        _focus?.Reset();
        _settings.PointerRingEnabled = false;
        _settings.LaserEnabled = false;
        _settings.ClickPulseEnabled = false;
        _settings.SpotlightEnabled = false;
        _settings.MagnifierEnabled = false;
        _overlay?.Hide();
        StopMagnifier();
        ExitAnnotation();
        if (_annotation is not null) _annotation.ClearMarks(); else _annotationDocument.Clear();
        Save();
        _toolbar?.Refresh();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();

    private void BeginFocus() => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Guard(() =>
    {
        if (IsRunning && !IsPaused && !IsAnnotating) _focus?.Begin();
    }));

    private void EndFocus() => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Guard(() => _focus?.End()));

    private void Toggle(Action action)
    {
        action();
        Save();
        RefreshOverlay();
        _toolbar?.Refresh();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySettings()
    {
        _settings.Normalize();
        Save();
        RefreshOverlay();
        _toolbar?.Refresh();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshOverlay()
    {
        if (IsAnnotating) { StopMagnifier(); _overlay?.Hide(); return; }
        if (IsRunning && !IsPaused && _settings.MagnifierEnabled)
        {
            if (_magnifier is null)
            {
                var magnifier = new CueGpuMagnifier(_settings);
                _magnifier = magnifier;
                magnifier.Error += exception => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (!ReferenceEquals(_magnifier, magnifier)) return;
                    _settings.MagnifierEnabled = false;
                    StopMagnifier();
                    Save();
                    _toolbar?.Refresh();
                    Error?.Invoke(this, new ModuleErrorEventArgs($"GPU 放大镜已停止：{exception.Message}"));
                });
                magnifier.Start();
            }
        }
        else StopMagnifier();

        if (!IsRunning || IsPaused || !IsOverlayActive)
        {
            _overlay?.Hide();
            return;
        }

        if (_overlay is null)
        {
            _overlay = new CueOverlayWindow(_settings);
            _overlay.Closed += (_, _) => _overlay = null;
        }

        _overlay.Refresh();
        _overlay.Show();
    }

    private void TemporarilyHideOverlays()
    {
        StopMagnifier();
        _overlay?.Hide();
        _toolbar?.Hide();
    }

    private void CaptureRectangle(System.Drawing.Rectangle bounds)
    {
        var toolbarWasVisible = _toolbar?.IsVisible == true;
        TemporarilyHideOverlays();
        string path;
        try
        {
            using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            }

            var root = string.IsNullOrWhiteSpace(_settings.ScreenshotFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Mujun Cue")
                : Environment.ExpandEnvironmentVariables(_settings.ScreenshotFolder);
            Directory.CreateDirectory(root);
            path = Path.Combine(root, $"Cue-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Forms.Clipboard.SetImage((System.Drawing.Image)bitmap.Clone());
        }
        finally
        {
            RefreshOverlay();
            if (toolbarWasVisible) _toolbar?.Show();
        }
        ScreenshotSaved?.Invoke(this, path);
    }

    public event EventHandler<string>? ScreenshotSaved;

    private void Save() => _saveSettings();

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            _focus?.Reset();
            Error?.Invoke(this, new ModuleErrorEventArgs($"Mujun Cue 操作失败：{exception.Message}"));
        }
    }

    private void DisposeRuntime()
    {
        IsAnnotating = false;
        if (_toolbar is not null) _toolbar.Owner = null;
        StopMagnifier();
        _input?.Dispose();
        _input = null;
        _focus?.Dispose();
        _focus = null;
        if (_overlay is not null)
        {
            _overlay.StopRendering();
            _overlay.Close();
            _overlay = null;
        }

        _annotation?.Close();
        _annotation = null;
        _toolbar?.Close();
        _toolbar = null;
    }

    private void StopMagnifier()
    {
        var magnifier = _magnifier;
        _magnifier = null;
        magnifier?.Dispose();
    }
}

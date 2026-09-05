using System.IO;
using System.Windows;
using PersonalToolbox.Modules;
using PersonalToolbox.Services;

namespace EffectCapture.TestApp;

public partial class App : System.Windows.Application
{
    private StandaloneSettingsStore? _settingsStore;
    private EffectCaptureModule? _capture;
    private GlobalHotkeyService? _hotkey;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _settingsStore = new StandaloneSettingsStore();
        var settings = _settingsStore.Load();
        var outputRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MU Dynamic Capture Test",
            "captures");

        var codexLauncher = new CodexDeepLinkLauncher();
        _capture = new EffectCaptureModule(
            settings,
            () => _settingsStore.Save(settings),
            codexLauncher.OpenEffectAsync,
            outputRoot);
        _window = new MainWindow(_capture, settings, outputRoot);
        MainWindow = _window;

        _capture.StateChanged += (_, _) => Dispatcher.BeginInvoke(() =>
            _window.SetCaptureState(_capture.IsBusy));
        _capture.Error += (_, error) => Dispatcher.BeginInvoke(() =>
            _window.SetStatus(error.Message, isError: true));
        _capture.SettingsChanged += (_, _) => ConfigureHotkey();

        _hotkey = new GlobalHotkeyService(() => Dispatcher.BeginInvoke(_capture.StartCapture));
        ConfigureHotkey();
        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _capture?.Dispose();
        base.OnExit(e);
    }

    private void ConfigureHotkey()
    {
        if (_hotkey?.TrySet(_window?.Settings.GlobalHotkey) == false)
        {
            _window?.SetStatus("全局快捷键已被其他程序占用；仍可用主按钮开始录制。", isError: true);
        }
    }
}

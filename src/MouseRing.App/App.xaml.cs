using System.Windows;
using MouseRing.Services;

namespace MouseRing;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private MouseRingModule? _module;
    private StartupRegistrationService? _startupService;
    private TrayIconService? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "Local\\MouseRing.App", out var isFirstInstance);
        _ownsMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _module = new MouseRingModule(isHosted: false);
        _module.Error += (_, error) => _trayIcon?.ShowError(error.Message);
        _startupService = new StartupRegistrationService();
        _startupService.SetEnabled(_module.Settings.RunAtStartup);
        _trayIcon = new TrayIconService(OpenSettings, RequestShutdown);
        _trayIcon.PauseChanged += paused => _module.SetPaused(paused);

        if (!_module.Start())
        {
            System.Windows.MessageBox.Show(
                "Orbit 已经在工具箱或另一个进程中运行。",
                "Orbit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RequestShutdown();
            return;
        }

        _trayIcon.ShowStartedNotice();
        if (e.Args.Any(argument => string.Equals(argument, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            Dispatcher.BeginInvoke(OpenSettings);
        }

        if (e.Args.Any(argument => string.Equals(argument, "--preview", StringComparison.OrdinalIgnoreCase)))
        {
            Dispatcher.BeginInvoke(() => _module.ShowPreview());
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _module?.Dispose();
        _trayIcon?.Dispose();

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OpenSettings()
    {
        if (_module?.OpenSettings() != true)
        {
            return;
        }

        if (_startupService?.SetEnabled(_module.Settings.RunAtStartup) == false)
        {
            _trayIcon?.ShowError("无法更新开机启动设置。");
        }
    }

    private void RequestShutdown() => Dispatcher.Invoke(Shutdown);
}

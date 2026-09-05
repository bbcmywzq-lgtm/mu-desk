using DesktopOrganizer.Services;

namespace DesktopOrganizer;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIconService? _trayIcon;
    private SingleInstanceActivationService? _activationService;
    private Mutex? _runtimeMutex;
    private bool _ownsRuntimeMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstanceMutex = new Mutex(true, "Local\\DesktopOrganizer.App", out var isFirstInstance);
        _ownsSingleInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            SingleInstanceActivationService.SignalExistingInstance();
            Shutdown();
            return;
        }

        _runtimeMutex = new Mutex(false, "Local\\DesktopOrganizer.Runtime");
        try
        {
            _ownsRuntimeMutex = _runtimeMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsRuntimeMutex = true;
        }

        if (!_ownsRuntimeMutex)
        {
            System.Windows.MessageBox.Show(
                "Grid 已经由个人工具箱承载。",
                "Grid",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        _trayIcon = new TrayIconService(window);
        _activationService = new SingleInstanceActivationService(() =>
            Dispatcher.BeginInvoke(() =>
            {
                window.ShowAndActivate();
                _trayIcon?.ShowRunningNotice();
            }));
        window.Show();
        _trayIcon.ShowStartedNotice();
        AppLog.Information("DesktopOrganizer started.");
    }

    public void RequestShutdown(int exitCode = 0)
    {
        if (MainWindow is MainWindow window)
        {
            window.PrepareForShutdown();
        }

        Shutdown(exitCode);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _activationService?.Dispose();
        _trayIcon?.Dispose();
        if (_ownsRuntimeMutex)
        {
            _runtimeMutex?.ReleaseMutex();
        }

        _runtimeMutex?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        AppLog.Information("DesktopOrganizer exited.");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show(
            "Grid 遇到无法恢复的错误，即将安全退出。诊断信息已保存在本地日志中。",
            "Grid",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
        RequestShutdown(-1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled application exception.", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Unobserved background task exception.", e.Exception);
        e.SetObserved();
    }
}

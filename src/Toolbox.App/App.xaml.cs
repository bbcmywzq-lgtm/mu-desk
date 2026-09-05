using System.IO;
using System.Windows;
using DesktopOrganizer;
using MouseRing;
using PersonalToolbox.Modules;
using PersonalToolbox.Services;
using Toolbox.Core;

namespace PersonalToolbox;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showWindowSignal;
    private RegisteredWaitHandle? _showWindowRegistration;
    private ToolboxSettingsStore? _settingsStore;
    private ToolboxStartupService? _startupService;
    private ToolboxSettings _settings = new();
    private MouseRingModule? _mouseRing;
    private CueModule? _cue;
    private EffectCaptureModule? _effectCapture;
    private GlobalHotkeyService? _effectHotkey;
    private CursorGalleryModule? _cursorGallery;
    private CispQuestionBankModule? _cispQuestionBank;
    private DesktopOrganizerModule? _desktopOrganizer;
    private FileShelfModule? _fileShelf;
    private FileShelfBridgeServer? _fileShelfBridge;
    private LightPetModule? _lightPet;
    private ReminderNotesModule? _reminderNotes;
    private MainWindow? _mainWindow;
    private ToolboxTrayIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var instanceSuffix = GetInstanceSuffix();
        var isolatedQa = instanceSuffix.Length > 0;

        _singleInstanceMutex = new Mutex(true, "Local\\PersonalToolbox.App" + instanceSuffix, out var isFirstInstance);
        _ownsMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting("Local\\PersonalToolbox.ShowWindow" + instanceSuffix);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The first instance is still starting and will show itself normally.
            }

            Shutdown();
            return;
        }

        _showWindowSignal = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            "Local\\PersonalToolbox.ShowWindow" + instanceSuffix);
        _showWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showWindowSignal,
            (_, _) => Dispatcher.BeginInvoke(() => _mainWindow?.ShowAndActivate()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _settingsStore = new ToolboxSettingsStore();
        _settings = _settingsStore.Load();
        _startupService = new ToolboxStartupService();
        if (instanceSuffix.Length == 0)
        {
            _startupService.RemoveLegacyEntries();
            _startupService.SetEnabled(_settings.RunAtStartup);
        }

        var codexLauncher = new CodexDeepLinkLauncher();
        _effectCapture = new EffectCaptureModule(
            _settings.EffectCapture,
            SaveSettings,
            codexLauncher.OpenEffectAsync);
        _effectCapture.Error += (_, error) => _trayIcon?.ShowError(error.Message);
        _effectCapture.StateChanged += (_, _) => Dispatcher.Invoke(UpdateUiState);
        _effectCapture.SettingsChanged += (_, _) => ConfigureEffectHotkey();
        _effectHotkey = new GlobalHotkeyService(() => Dispatcher.BeginInvoke(_effectCapture.StartCapture));
        ConfigureEffectHotkey();
        _mouseRing = new MouseRingModule(isHosted: true, effectCapture: _effectCapture.StartCapture);
        _mouseRing.Error += (_, error) => _trayIcon?.ShowError(error.Message);
        _mouseRing.StateChanged += (_, _) => Dispatcher.Invoke(UpdateUiState);
        _cue = new CueModule(_settings.Cue, SaveSettings);
        _cue.Error += (_, error) => _trayIcon?.ShowError(error.Message);
        _cue.StateChanged += (_, _) => Dispatcher.Invoke(UpdateUiState);
        _cue.ScreenshotSaved += (_, path) => _trayIcon?.ShowInfo("Mujun Cue", $"截图已保存：{path}");
        _cursorGallery = new CursorGalleryModule();
        _cursorGallery.Error += (_, error) => _trayIcon?.ShowError(error.Message);
        _cursorGallery.Start();
        _cispQuestionBank = new CispQuestionBankModule();
        _cispQuestionBank.Error += (_, error) => _trayIcon?.ShowError(error.Message);
        _cispQuestionBank.Start();
        _desktopOrganizer = new DesktopOrganizerModule();
        _desktopOrganizer.Error += (_, error) => _trayIcon?.ShowError(error.Message);
        _desktopOrganizer.StateChanged += (_, _) => Dispatcher.Invoke(UpdateUiState);
        _fileShelf = new FileShelfModule(_settings.FileShelf, SaveSettings);
        _fileShelf.Error += (_, error) => _trayIcon?.ShowError(error.Message);
        _fileShelf.StateChanged += (_, _) => Dispatcher.Invoke(UpdateUiState);
        _lightPet = new LightPetModule();
        _lightPet.StateChanged += (_, _) => Dispatcher.Invoke(UpdateUiState);
        _reminderNotes = new ReminderNotesModule();

        _mainWindow = new MainWindow(
            _mouseRing,
            _cue,
            _effectCapture,
            _cursorGallery,
            _cispQuestionBank,
            _desktopOrganizer,
            _fileShelf,
            _lightPet,
            _reminderNotes,
            _settings);
        MainWindow = _mainWindow;
        _mainWindow.MouseRingEnabledChanged += SetMouseRingEnabled;
        _mainWindow.CueEnabledChanged += SetCueEnabled;
        _mainWindow.DesktopOrganizerEnabledChanged += SetDesktopOrganizerEnabled;
        _mainWindow.FileShelfEnabledChanged += SetFileShelfEnabled;
        _mainWindow.LightPetEnabledChanged += SetLightPetEnabled;
        _mainWindow.StartupChanged += SetStartupEnabled;

        _trayIcon = new ToolboxTrayIcon(
            () => Dispatcher.Invoke(_mainWindow.ShowAndActivate),
            enabled => Dispatcher.Invoke(() => SetMouseRingEnabled(enabled)),
            () => Dispatcher.Invoke(() => _mouseRing.OpenSettings()),
            enabled => Dispatcher.Invoke(() => SetCueEnabled(enabled)),
            () => Dispatcher.Invoke(_cue.OpenToolbar),
            () => Dispatcher.Invoke(_cue.OpenSettings),
            () => Dispatcher.Invoke(_cue.ResetAll),
            () => Dispatcher.Invoke(_effectCapture.StartCapture),
            () => Dispatcher.Invoke(() => _effectCapture.OpenSettings(_mainWindow)),
            () => Dispatcher.Invoke(() => _cispQuestionBank.OpenSettings()),
            () => Dispatcher.Invoke(() => _cursorGallery.OpenSettings()),
            () => Dispatcher.Invoke(() => OpenReminderNotes(ReminderNotesPage.Reminder)),
            () => Dispatcher.Invoke(() => OpenReminderNotes(ReminderNotesPage.Note)),
            () => Dispatcher.Invoke(ToggleFileShelfFromTray),
            enabled => Dispatcher.Invoke(() => SetLightPetEnabled(enabled)),
            () => Dispatcher.Invoke(ToggleLightPetVisibility),
            () => Dispatcher.Invoke(OpenLightPetSettings),
            enabled => Dispatcher.Invoke(() => SetDesktopOrganizerEnabled(enabled)),
            () => Dispatcher.Invoke(() => _desktopOrganizer.ShowDesktop()),
            () => Dispatcher.Invoke(() => _desktopOrganizer.OpenSettings()),
            paused => Dispatcher.Invoke(() => SetAllPaused(paused)),
            () => Dispatcher.Invoke(RequestShutdown));

        if (!_reminderNotes.StartBackground())
        {
            _trayIcon.ShowError("没有找到“Memo”模块；主页其他工具仍可使用。");
        }

        if (_settings.LightPetEnabled && !_lightPet.Start())
        {
            _settings.LightPetEnabled = false;
            _settingsStore.Save(_settings);
            _trayIcon.ShowError("没有找到 Pal；主页其他工具仍可使用。");
        }

        if (!isolatedQa && _settings.MouseRingEnabled && !_mouseRing.Start())
        {
            _settings.MouseRingEnabled = false;
            _settingsStore.Save(_settings);
        }

        if (!isolatedQa && _settings.Cue.Enabled && !_cue.Start())
        {
            _settings.Cue.Enabled = false;
            _settingsStore.Save(_settings);
        }

        if (!isolatedQa && _settings.DesktopOrganizerEnabled && !_desktopOrganizer.Start())
        {
            _settings.DesktopOrganizerEnabled = false;
            _settingsStore.Save(_settings);
        }

        if (!isolatedQa && _settings.FileShelf.Enabled && !_fileShelf.Start())
        {
            _settings.FileShelf.Enabled = false;
            _settingsStore.Save(_settings);
        }

        if (!isolatedQa)
        {
            _fileShelfBridge = new FileShelfBridgeServer(request =>
                Dispatcher.InvokeAsync(() => HandleFileShelfBridgeRequest(request)).Task);
            _fileShelfBridge.Start();
        }

        _settingsStore.Save(_settings);

        UpdateUiState();
        var minimizedArgument = e.Args.Any(argument =>
            string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));
        var showArgument = e.Args.Any(argument =>
            string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase));
        if (showArgument || instanceSuffix.Length > 0 || (!minimizedArgument && !_settings.StartMinimized))
        {
            _mainWindow.ShowAndActivate();
        }

        _trayIcon.ShowStartedNotice();
    }

    private static string GetInstanceSuffix()
    {
        var value = Environment.GetEnvironmentVariable("MUDESK_INSTANCE_SUFFIX");
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var safe = new string(value.Where(char.IsLetterOrDigit).Take(32).ToArray());
        return safe.Length == 0 ? string.Empty : $".{safe}";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mouseRing?.Dispose();
        _cue?.Dispose();
        _effectCapture?.Dispose();
        _effectHotkey?.Dispose();
        _cursorGallery?.Dispose();
        _cispQuestionBank?.Dispose();
        _desktopOrganizer?.Dispose();
        _fileShelfBridge?.Dispose();
        _fileShelf?.Dispose();
        _lightPet?.Dispose();
        _reminderNotes?.Dispose();
        _trayIcon?.Dispose();
        _showWindowRegistration?.Unregister(null);
        _showWindowSignal?.Dispose();

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void SetMouseRingEnabled(bool enabled)
    {
        if (_mouseRing is null)
        {
            return;
        }

        if (enabled)
        {
            enabled = _mouseRing.Start();
        }
        else
        {
            _mouseRing.Stop();
        }

        _settings.MouseRingEnabled = enabled;
        SaveSettings();
        UpdateUiState();
    }

    private void SetCueEnabled(bool enabled)
    {
        if (_cue is null)
        {
            return;
        }

        if (enabled)
        {
            enabled = _cue.Start();
        }
        else
        {
            _cue.Stop();
        }

        _settings.Cue.Enabled = enabled;
        SaveSettings();
        UpdateUiState();
    }

    private void SetStartupEnabled(bool enabled)
    {
        if (_startupService?.SetEnabled(enabled) != true)
        {
            _trayIcon?.ShowError("无法更新开机启动设置。");
            UpdateUiState();
            return;
        }

        _settings.RunAtStartup = enabled;
        SaveSettings();
        UpdateUiState();
    }

    private void SetDesktopOrganizerEnabled(bool enabled)
    {
        if (_desktopOrganizer is null)
        {
            return;
        }

        if (enabled)
        {
            enabled = _desktopOrganizer.Start();
        }
        else
        {
            _desktopOrganizer.Stop();
        }

        _settings.DesktopOrganizerEnabled = enabled;
        SaveSettings();
        UpdateUiState();
    }

    private void SetAllPaused(bool paused)
    {
        if (_mouseRing?.IsRunning == true)
        {
            _mouseRing.SetPaused(paused);
        }

        if (_desktopOrganizer?.IsRunning == true)
        {
            _desktopOrganizer.SetPaused(paused);
        }

        if (_cue?.IsRunning == true)
        {
            _cue.SetPaused(paused);
        }

        if (_fileShelf?.IsRunning == true)
        {
            _fileShelf.SetPaused(paused);
        }

        UpdateUiState();
    }

    private void UpdateUiState()
    {
        _mainWindow?.RefreshState();
        _trayIcon?.UpdateState(
            _settings.MouseRingEnabled && _mouseRing?.IsRunning == true,
            _settings.Cue.Enabled && _cue?.IsRunning == true,
            _settings.DesktopOrganizerEnabled && _desktopOrganizer?.IsRunning == true,
            _settings.FileShelf.Enabled && _fileShelf?.IsRunning == true,
            _settings.LightPetEnabled && _lightPet?.IsRunning == true,
            _mouseRing?.IsPaused == true || _cue?.IsPaused == true || _desktopOrganizer?.IsPaused == true || _fileShelf?.IsPaused == true);
    }

    private void SaveSettings()
    {
        if (_settingsStore?.Save(_settings) == false)
        {
            _trayIcon?.ShowError("工具箱设置无法写入本地配置文件。");
        }
    }

    private void ConfigureEffectHotkey()
    {
        if (_effectHotkey?.TrySet(_settings.EffectCapture.GlobalHotkey) == false)
        {
            _trayIcon?.ShowError("Clip 快捷键已被其他程序占用；主页、托盘和 Orbit 仍可使用。");
        }
    }

    private void SetFileShelfEnabled(bool enabled)
    {
        if (_fileShelf is null)
        {
            return;
        }

        if (enabled)
        {
            enabled = _fileShelf.Start();
        }
        else
        {
            _fileShelf.Stop();
        }

        _settings.FileShelf.Enabled = enabled;
        SaveSettings();
        UpdateUiState();
    }

    private void SetLightPetEnabled(bool enabled)
    {
        if (_lightPet is null)
        {
            return;
        }

        if (enabled)
        {
            enabled = _lightPet.Start();
        }
        else
        {
            _lightPet.Stop();
        }

        _settings.LightPetEnabled = enabled;
        SaveSettings();
        UpdateUiState();
    }

    private void ToggleLightPetVisibility()
    {
        if (_lightPet?.ToggleVisibility() != true)
        {
            _trayIcon?.ShowError("Pal 当前不可用。");
        }
    }

    private void OpenLightPetSettings()
    {
        if (_lightPet?.OpenSettings() != true)
        {
            _trayIcon?.ShowError("Pal 当前不可用。");
        }
    }

    private void ToggleFileShelfFromTray()
    {
        if (_fileShelf is null || _mainWindow is null)
        {
            return;
        }

        if (!_settings.FileShelf.Enabled || !_fileShelf.IsRunning)
        {
            _mainWindow.ShowAndActivate();
            return;
        }

        _fileShelf.ToggleShelf();
    }

    private FileShelfBridgeResponse HandleFileShelfBridgeRequest(FileShelfBridgeRequest request)
    {
        if (_fileShelf is null || !_settings.FileShelf.Enabled || !_fileShelf.IsRunning)
        {
            return new FileShelfBridgeResponse(
                false,
                0,
                _fileShelf?.ItemCount ?? 0,
                "请先在 MU Desk 中启用 Drop。");
        }

        if (string.Equals(request.Command, "status", StringComparison.OrdinalIgnoreCase))
        {
            return new FileShelfBridgeResponse(
                true,
                0,
                _fileShelf.ItemCount,
                "Drop 连接正常。");
        }

        if (string.Equals(request.Command, "show", StringComparison.OrdinalIgnoreCase))
        {
            _fileShelf.ShowShelf();
            return new FileShelfBridgeResponse(
                true,
                0,
                _fileShelf.ItemCount,
                $"Drop · {_fileShelf.ItemCount} 项");
        }

        if (!string.Equals(request.Command, "addPaths", StringComparison.OrdinalIgnoreCase) ||
            request.Paths is not { Length: > 0 })
        {
            return new FileShelfBridgeResponse(
                false,
                0,
                _fileShelf.ItemCount,
                "Drop 没有收到有效的本地路径。");
        }

        var before = _fileShelf.ItemCount;
        var success = _fileShelf.TryAddPaths(request.Paths, out var message);
        var added = Math.Max(0, _fileShelf.ItemCount - before);
        if (success)
        {
            _fileShelf.ShowShelf();
        }

        return new FileShelfBridgeResponse(
            success,
            added,
            _fileShelf.ItemCount,
            message);
    }

    private void OpenReminderNotes(ReminderNotesPage page)
    {
        if (_reminderNotes?.Open(page) != true)
        {
            _trayIcon?.ShowError("没有找到“Memo”模块。请重新发布或安装 MU Desk。");
        }
    }

    private void RequestShutdown()
    {
        if (_effectCapture?.IsBusy == true)
        {
            var answer = System.Windows.MessageBox.Show(
                "Clip 仍在录制或处理。现在退出会中断当前工作，但已经完成的本地素材包会保留。",
                "退出 MU Desk？",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _mainWindow?.PrepareForShutdown();
        Shutdown();
    }
}

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopOrganizer.Core.Models;
using DesktopOrganizer.Core.Services;
using DesktopOrganizer.Models;
using DesktopOrganizer.Services;
using DesktopOrganizer.Views;

namespace DesktopOrganizer;

public partial class MainWindow : System.Windows.Window
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;

    private readonly DesktopHostService _desktopHost = new();
    private readonly DesktopIconVisibilityService _desktopIconVisibility = new();
    private readonly LayoutStore _layoutStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly StartupRegistrationService _startupRegistration = new();
    private readonly BackupService _backupService = new();
    private readonly DesktopCatalog _desktopCatalog = new();
    private readonly SmartFolderCatalog _smartFolderCatalog = new();
    private readonly DesktopChangeMonitor _desktopChangeMonitor = new();
    private readonly FolderChangeMonitor _smartFolderMonitor = new();
    private readonly FileOperationService _fileOperationService = new();
    private readonly WallpaperService _wallpaperService = new();
    private readonly WorkspaceUndoManager _history = new();
    private readonly DispatcherTimer _reconnectTimer;
    private readonly DispatcherTimer _desktopRefreshTimer;
    private readonly DispatcherTimer _smartRefreshTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly bool _previewMode;
    private readonly bool _isHosted;
    private readonly Dictionary<string, ZoneView> _zoneViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<DesktopEntry>> _smartEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _smartErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _smartScanCancellations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _smartCurrentPaths = new(StringComparer.Ordinal);

    private HwndSource? _hwndSource;
    private LayoutSnapshot _snapshot = new();
    private AppSettings _settings = new();
    private IReadOnlyList<DesktopEntry> _entries = [];
    private System.Windows.Media.ImageSource? _wallpaper;
    private string _searchQuery = string.Empty;
    private string? _focusedZoneId;
    private int _nextZIndex = 1;
    private bool _allowClose;

    public MainWindow(bool isHosted = false)
    {
        InitializeComponent();

        _isHosted = isHosted;

        _previewMode = Environment.GetCommandLineArgs().Contains("--windowed-preview", StringComparer.OrdinalIgnoreCase);
        if (_previewMode)
        {
            AllowsTransparency = false;
            Background = new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(11, 16, 24),
                System.Windows.Media.Color.FromRgb(20, 28, 41),
                135);
            WindowStyle = System.Windows.WindowStyle.SingleBorderWindow;
            ResizeMode = System.Windows.ResizeMode.CanResize;
            ShowInTaskbar = true;
            Title = "Grid · 窗口预览";
        }

        if (!_previewMode)
        {
            SourceInitialized += OnSourceInitialized;
        }

        Loaded += OnLoaded;
        SizeChanged += OnWindowSizeChanged;
        Closing += OnClosing;

        _reconnectTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            OnReconnectTick,
            Dispatcher);
        _desktopRefreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(400),
            DispatcherPriority.Background,
            OnDesktopRefreshTick,
            Dispatcher);
        _smartRefreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(550),
            DispatcherPriority.Background,
            OnSmartRefreshTick,
            Dispatcher);
        _toastTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(7),
            DispatcherPriority.Background,
            (_, _) => HideOperationToast(),
            Dispatcher);
        _desktopChangeMonitor.Changed += OnDesktopChanged;
        _desktopChangeMonitor.Renamed += OnDesktopRenamed;
        _smartFolderMonitor.Changed += OnSmartFolderChanged;
    }

    public void CreateZone()
    {
        RecordHistory();
        var sequence = 1;
        while (_snapshot.Zones.Any(zone => string.Equals(
                   zone.Name,
                   $"新分区 {sequence}",
                   StringComparison.CurrentCultureIgnoreCase)))
        {
            sequence++;
        }

        var column = sequence % 2;
        var row = sequence / 2;
        var layout = new ZoneLayout
        {
            Name = $"新分区 {sequence}",
            IsInbox = !_snapshot.Zones.Any(zone => zone.Kind == ZoneKind.Desktop),
            X = 70 + column * 590,
            Y = 72 + row * 340,
            Width = 500,
            Height = 320,
        };
        layout.ClampTo(ActualWidth, ActualHeight);

        _snapshot.Zones.Add(layout);
        var view = CreateZoneView(layout);
        RefreshZoneItems();
        SaveSnapshot();

        Dispatcher.BeginInvoke(view.BeginRename, DispatcherPriority.Input);
    }

    public void CreateSmartZone()
    {
        var editor = new SmartZoneEditorWindow
        {
            Owner = _previewMode ? this : null,
            WindowStartupLocation = _previewMode
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
        };
        if (editor.ShowDialog() != true || editor.Result is null)
        {
            return;
        }

        RecordHistory();
        var layout = editor.Result.Clone();
        var sequence = _snapshot.Zones.Count + 1;
        layout.X = 70 + (sequence % 2) * 590;
        layout.Y = 72 + (sequence / 2) * 340;
        layout.ClampTo(ActualWidth, ActualHeight);
        _snapshot.Zones.Add(layout);
        CreateZoneView(layout);
        SaveSnapshot();
        RestartSmartFolderMonitoring();
        _ = RefreshSmartZoneAsync(layout);
    }

    public void ResetPrototypeLayout()
    {
        var result = System.Windows.MessageBox.Show(
            "重置全部分区、规则和项目归类？\n\n真实桌面文件不会受到影响，此操作可以撤销。",
            "重置布局",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        RecordHistory();
        _snapshot = CreateDefaultSnapshot();
        BuildZoneViews();
        RefreshZoneItems();
        ApplyWallpaperBackdrop();
        SaveSnapshot();
        RestartSmartFolderMonitoring();
        _ = RefreshAllSmartZonesAsync();
    }

    public void FocusSearch()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (_previewMode)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            return;
        }

        _desktopHost.EnsureAttached(new WindowInteropHelper(this).Handle);
    }

    public void PrepareForShutdown() => _allowClose = true;

    public void UndoLastAction()
    {
        var previous = _history.Undo();
        if (previous is null)
        {
            return;
        }

        if (previous.FileMoves.Count > 0)
        {
            var undoResult = _fileOperationService.UndoMoves(previous.FileMoves);
            if (undoResult.Failures.Count > 0)
            {
                ShowFileOperationFailures("部分文件无法撤销", undoResult.Failures);
            }
        }

        _snapshot = previous.Snapshot.Clone();
        NormalizeSnapshot();
        _entries = _desktopCatalog.LoadEntries();
        BuildZoneViews();
        RefreshZoneItems();
        ApplyWallpaperBackdrop();
        SaveSnapshot();
        RestartSmartFolderMonitoring();
        _ = RefreshAllSmartZonesAsync();
        UpdateUndoState();
        HideOperationToast();
    }

    public void ApplyRulesNow()
    {
        if (!_snapshot.Rules.Any(rule => rule.IsEnabled))
        {
            System.Windows.MessageBox.Show(
                "尚未启用任何自动整理规则，请先在设置中创建规则。",
                "自动整理",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RecordHistory();
        if (!ApplyOrganizationRules())
        {
            _history.Undo();
            UpdateUndoState();
            return;
        }

        RefreshZoneItems();
        SaveSnapshot();
    }

    public void OpenSettings()
    {
        var settingsWindow = new SettingsWindow(
            _settings,
            _snapshot.Zones,
            _snapshot.Rules,
            ExportData,
            ImportData,
            _isHosted)
        {
            Owner = _previewMode ? this : null,
            WindowStartupLocation = _previewMode
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
        };
        if (settingsWindow.ShowDialog() != true)
        {
            return;
        }

        var oldSettings = _settings;
        var rulesChanged = !RulesEquivalent(_snapshot.Rules, settingsWindow.Rules);
        if (rulesChanged)
        {
            RecordHistory();
            _snapshot.Rules = settingsWindow.Rules.Select(rule => rule.Clone()).ToList();
        }

        _settings = settingsWindow.Settings.Clone();
        if (!_isHosted &&
            oldSettings.LaunchAtStartup != _settings.LaunchAtStartup &&
            !_startupRegistration.SetEnabled(_settings.LaunchAtStartup))
        {
            _settings.LaunchAtStartup = oldSettings.LaunchAtStartup;
            System.Windows.MessageBox.Show(
                "无法更新 Windows 开机启动设置，其他设置已经保存。",
                "Grid",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _settingsStore.Save(_settings);
        ApplySettingsToWindow();
        var placementsChanged = ApplyOrganizationRules();
        if (rulesChanged || placementsChanged)
        {
            RefreshZoneItems();
            SaveSnapshot();
        }
    }

    public void ExportData()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 Grid 备份",
            Filter = "Grid 备份 (*.desktoporganizer)|*.desktoporganizer",
            DefaultExt = ".desktoporganizer",
            AddExtension = true,
            FileName = $"QIGE-{DateTime.Now:yyyyMMdd}.desktoporganizer",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var succeeded = _backupService.Export(dialog.FileName, _snapshot, _settings);
        System.Windows.MessageBox.Show(
            succeeded ? "备份已经导出。" : "无法导出备份，请查看诊断日志。",
            "Grid",
            MessageBoxButton.OK,
            succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    public bool ImportData()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 Grid 备份",
            Filter = "Grid 备份 (*.desktoporganizer)|*.desktoporganizer",
            DefaultExt = ".desktoporganizer",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var bundle = _backupService.Import(dialog.FileName);
        if (bundle is null)
        {
            System.Windows.MessageBox.Show(
                "备份文件无效、损坏或来自不支持的版本。",
                "无法导入",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var confirmation = System.Windows.MessageBox.Show(
            "导入将替换当前布局、规则和设置。\n\n系统会先自动保存当前恢复点，真实桌面文件不会受到影响。",
            "导入备份",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return false;
        }

        if (!_backupService.CreateRecoveryPoint(_snapshot, _settings))
        {
            System.Windows.MessageBox.Show(
                "无法创建导入前恢复点，因此已取消导入。",
                "无法导入",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var oldLaunchAtStartup = _settings.LaunchAtStartup;
        _snapshot = bundle.Layout.Clone();
        _settings = bundle.Settings.Clone();
        NormalizeSnapshot();
        if (_isHosted)
        {
            _settings.LaunchAtStartup = false;
        }
        else if (oldLaunchAtStartup != _settings.LaunchAtStartup &&
            !_startupRegistration.SetEnabled(_settings.LaunchAtStartup))
        {
            _settings.LaunchAtStartup = oldLaunchAtStartup;
        }

        _history.Clear();
        SearchTextBox.Clear();
        BuildZoneViews();
        RefreshZoneItems();
        ApplyWallpaperBackdrop();
        ApplySettingsToWindow();
        _settingsStore.Save(_settings);
        SaveSnapshot();
        RestartSmartFolderMonitoring();
        _ = RefreshAllSmartZonesAsync();
        UpdateUndoState();
        return true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WindowMessageHook);
        _desktopHost.Attach(handle);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_previewMode)
        {
            _desktopIconVisibility.RecoverAfterUnexpectedExit();
        }

        _settings = _settingsStore.Load();
        if (_isHosted && _settings.LaunchAtStartup)
        {
            _settings.LaunchAtStartup = false;
            _settingsStore.Save(_settings);
        }
        _snapshot = _layoutStore.Load() ?? CreateDefaultSnapshot();
        NormalizeSnapshot();
        _entries = _desktopCatalog.LoadEntries();

        if (_settings.AutoApplyRules && ApplyOrganizationRules())
        {
            SaveSnapshot();
        }

        BuildZoneViews();
        RefreshZoneItems();
        ClampZonesToWorkspace();
        ApplyWallpaperBackdrop();
        _desktopChangeMonitor.Start(_desktopCatalog.GetDesktopDirectories());
        RestartSmartFolderMonitoring();
        _ = RefreshAllSmartZonesAsync();
        ApplySettingsToWindow();
        _reconnectTimer.Start();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            AppLog.Information("Main window hidden; application remains available in the notification area.");
            return;
        }

        _reconnectTimer.Stop();
        _desktopRefreshTimer.Stop();
        _smartRefreshTimer.Stop();
        _toastTimer.Stop();
        _desktopChangeMonitor.Dispose();
        _smartFolderMonitor.Dispose();
        CancelSmartScans();
        _desktopIconVisibility.Restore();
        SaveSnapshot();
        if (!_previewMode)
        {
            _desktopHost.Detach(new WindowInteropHelper(this).Handle);
            _hwndSource?.RemoveHook(WindowMessageHook);
        }
    }

    private void OnReconnectTick(object? sender, EventArgs e)
    {
        if (_previewMode)
        {
            return;
        }

        _desktopHost.EnsureAttached(new WindowInteropHelper(this).Handle);
        if (_settings.HideNativeDesktopIcons)
        {
            _desktopIconVisibility.EnsureHidden();
        }
        ClampZonesToWorkspace();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_wallpaper is not null)
        {
            foreach (var view in _zoneViews.Values)
            {
                view.SetBackdrop(_wallpaper, e.NewSize);
            }
        }

        ClampZonesToWorkspace();
    }

    private void OnNewZoneMenuClick(object sender, RoutedEventArgs e)
    {
        if (NewZoneButton.ContextMenu is null)
        {
            return;
        }

        NewZoneButton.ContextMenu.PlacementTarget = NewZoneButton;
        NewZoneButton.ContextMenu.IsOpen = true;
    }

    private void OnNewDesktopZoneClick(object sender, RoutedEventArgs e) => CreateZone();

    private void OnNewSmartZoneClick(object sender, RoutedEventArgs e) => CreateSmartZone();

    private void OnResetLayoutClick(object sender, RoutedEventArgs e) => ResetPrototypeLayout();

    private void OnUndoClick(object sender, RoutedEventArgs e) => UndoLastAction();

    private void OnToastUndoClick(object sender, RoutedEventArgs e) => UndoLastAction();

    private void OnApplyRulesClick(object sender, RoutedEventArgs e) => ApplyRulesNow();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control) &&
            e.Key == System.Windows.Input.Key.F)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control) &&
            e.Key == System.Windows.Input.Key.Z &&
            System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
        {
            UndoLastAction();
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.Escape && _focusedZoneId is not null)
        {
            ExitFocusMode();
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.Escape && _searchQuery.Length > 0)
        {
            ClearSearch();
            e.Handled = true;
        }
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchQuery = SearchTextBox.Text.Trim();
        SearchHint.Visibility = _searchQuery.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = _searchQuery.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_snapshot.Zones.Count > 0)
        {
            RefreshZoneItems();
        }
    }

    private void OnSearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (_focusedZoneId is not null)
            {
                ExitFocusMode();
            }
            else
            {
                ClearSearch();
            }

            e.Handled = true;
        }
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e) => ClearSearch();

    private void ClearSearch()
    {
        SearchTextBox.Clear();
        SearchTextBox.Focus();
    }

    private void OnDesktopChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _desktopRefreshTimer.Stop();
            _desktopRefreshTimer.Start();
        });
    }

    private void OnSmartFolderChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _smartRefreshTimer.Stop();
            _smartRefreshTimer.Start();
        });
    }

    private async void OnSmartRefreshTick(object? sender, EventArgs e)
    {
        _smartRefreshTimer.Stop();
        await RefreshAllSmartZonesAsync();
    }

    private void RestartSmartFolderMonitoring() => _smartFolderMonitor.Start(_snapshot.Zones);

    private async Task RefreshAllSmartZonesAsync()
    {
        var smartZones = _snapshot.Zones
            .Where(zone => zone.Kind == ZoneKind.SmartFolder)
            .Select(zone => zone.Clone())
            .ToArray();
        await Task.WhenAll(smartZones.Select(RefreshSmartZoneAsync));
    }

    private async Task RefreshSmartZoneAsync(ZoneLayout zone)
    {
        if (zone.Kind != ZoneKind.SmartFolder || !_zoneViews.ContainsKey(zone.Id))
        {
            return;
        }

        if (_smartScanCancellations.Remove(zone.Id, out var previousCancellation))
        {
            previousCancellation.Cancel();
            previousCancellation.Dispose();
        }

        var cancellation = new CancellationTokenSource();
        _smartScanCancellations[zone.Id] = cancellation;
        _smartErrors[zone.Id] = null;
        var currentPath = GetSmartCurrentPath(zone);
        if (_zoneViews.TryGetValue(zone.Id, out var view))
        {
            view.SetNavigationPath(zone.SourcePath, currentPath);
            view.SetStatus("正在后台扫描…");
        }

        var progress = new Progress<IReadOnlyList<DesktopEntry>>(entries =>
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            _smartEntries[zone.Id] = entries;
            RefreshZoneItems();
        });

        try
        {
            var result = await _smartFolderCatalog.ScanAsync(zone, currentPath, progress, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            _smartEntries[zone.Id] = result.Entries;
            _smartErrors[zone.Id] = result.Error;
            if (result.IsLarge)
            {
                AppLog.Warning($"Smart zone '{zone.Name}' contains at least 10,000 matching files.");
            }

            RefreshZoneItems();
        }
        catch (OperationCanceledException)
        {
            // A newer scan superseded this one.
        }
        finally
        {
            if (_smartScanCancellations.TryGetValue(zone.Id, out var current) && ReferenceEquals(current, cancellation))
            {
                _smartScanCancellations.Remove(zone.Id);
                cancellation.Dispose();
            }
        }
    }

    private void CancelSmartScans()
    {
        foreach (var cancellation in _smartScanCancellations.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _smartScanCancellations.Clear();
    }

    private void OnDesktopRenamed(object? sender, DesktopPathRenamedEventArgs e)
    {
        if (LayoutStateManager.RenameItemPath(_snapshot, e.OldPath, e.NewPath))
        {
            SaveSnapshot();
        }
    }

    private void OnDesktopRefreshTick(object? sender, EventArgs e)
    {
        _desktopRefreshTimer.Stop();
        _entries = _desktopCatalog.LoadEntries();

        var currentPaths = _entries.Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedPlacements = _snapshot.Placements.RemoveAll(placement => !currentPaths.Contains(placement.Path));
        var rulesChanged = _settings.AutoApplyRules && ApplyOrganizationRules();
        RefreshZoneItems();
        if (removedPlacements > 0 || rulesChanged)
        {
            SaveSnapshot();
        }
    }

    private static LayoutSnapshot CreateDefaultSnapshot()
    {
        return new LayoutSnapshot
        {
            Zones =
            [
                new ZoneLayout
                {
                    Name = "收件箱",
                    IsInbox = true,
                },
            ],
        };
    }

    private void NormalizeSnapshot()
    {
        LayoutStateManager.Normalize(_snapshot);
    }

    private void BuildZoneViews()
    {
        WorkspaceCanvas.Children.Clear();
        _zoneViews.Clear();
        _nextZIndex = 1;

        foreach (var layout in _snapshot.Zones)
        {
            CreateZoneView(layout);
        }

        ApplyFocusMode();
    }

    private ZoneView CreateZoneView(ZoneLayout layout)
    {
        var view = new ZoneView();
        view.ApplyLayout(layout);
        view.LayoutChanged += OnZoneLayoutChanged;
        view.ItemDropped += OnZoneItemDropped;
        view.ReturnToInboxRequested += OnReturnToInboxRequested;
        view.DeleteRequested += OnZoneDeleteRequested;
        view.Activated += OnZoneActivated;
        view.PinToggleRequested += OnZonePinToggleRequested;
        view.FocusToggleRequested += OnZoneFocusToggleRequested;
        view.EditSmartZoneRequested += OnEditSmartZoneRequested;
        view.RenameFileRequested += OnRenameFileRequested;
        view.RecycleFilesRequested += OnRecycleFilesRequested;
        view.NavigateFolderRequested += OnNavigateFolderRequested;
        view.NavigateRootRequested += OnNavigateRootRequested;
        view.NavigateUpRequested += OnNavigateUpRequested;
        System.Windows.Controls.Panel.SetZIndex(view, _nextZIndex++);
        WorkspaceCanvas.Children.Add(view);
        _zoneViews[layout.Id] = view;
        view.ApplyFocusState(_focusedZoneId is not null, layout.Id == _focusedZoneId);
        if (layout.Kind == ZoneKind.SmartFolder)
        {
            view.SetNavigationPath(layout.SourcePath, GetSmartCurrentPath(layout));
        }

        if (_wallpaper is not null)
        {
            view.SetBackdrop(_wallpaper, new System.Windows.Size(ActualWidth, ActualHeight));
        }

        return view;
    }

    private void OnZoneLayoutChanged(object? sender, ZoneLayoutChangedEventArgs e)
    {
        var index = _snapshot.Zones.FindIndex(zone => zone.Id == e.Layout.Id);
        if (index < 0)
        {
            return;
        }

        _snapshot.Zones[index] = e.Layout.Clone();
        SaveSnapshot();
    }

    private void OnZonePinToggleRequested(object? sender, EventArgs e)
    {
        if (sender is not ZoneView view)
        {
            return;
        }

        var index = _snapshot.Zones.FindIndex(zone => zone.Id == view.ZoneId);
        if (index < 0)
        {
            return;
        }

        RecordHistory();
        _snapshot.Zones[index].IsPinned = !_snapshot.Zones[index].IsPinned;
        view.ApplyLayout(_snapshot.Zones[index]);
        SaveSnapshot();
    }

    private async void OnNavigateFolderRequested(object? sender, ItemPathEventArgs e)
    {
        if (sender is not ZoneView view)
        {
            return;
        }

        var zone = _snapshot.Zones.FirstOrDefault(candidate =>
            candidate.Id == view.ZoneId && candidate.Kind == ZoneKind.SmartFolder);
        if (zone is null || !Directory.Exists(e.Path) ||
            !SmartFolderCatalog.IsPathWithinRoot(zone.SourcePath, e.Path))
        {
            return;
        }

        _smartCurrentPaths[zone.Id] = Path.GetFullPath(e.Path);
        await RefreshSmartZoneAsync(zone.Clone());
    }

    private async void OnNavigateRootRequested(object? sender, EventArgs e)
    {
        if (sender is not ZoneView view)
        {
            return;
        }

        var zone = _snapshot.Zones.FirstOrDefault(candidate =>
            candidate.Id == view.ZoneId && candidate.Kind == ZoneKind.SmartFolder);
        if (zone is null)
        {
            return;
        }

        _smartCurrentPaths[zone.Id] = SmartFolderCatalog.ResolveNavigationPath(zone, zone.SourcePath);
        await RefreshSmartZoneAsync(zone.Clone());
    }

    private async void OnNavigateUpRequested(object? sender, EventArgs e)
    {
        if (sender is not ZoneView view)
        {
            return;
        }

        var zone = _snapshot.Zones.FirstOrDefault(candidate =>
            candidate.Id == view.ZoneId && candidate.Kind == ZoneKind.SmartFolder);
        if (zone is null)
        {
            return;
        }

        var currentPath = GetSmartCurrentPath(zone);
        var parentPath = Directory.GetParent(currentPath)?.FullName;
        _smartCurrentPaths[zone.Id] = parentPath is not null &&
                                      SmartFolderCatalog.IsPathWithinRoot(zone.SourcePath, parentPath)
            ? parentPath
            : SmartFolderCatalog.ResolveNavigationPath(zone, zone.SourcePath);
        await RefreshSmartZoneAsync(zone.Clone());
    }

    private string GetSmartCurrentPath(ZoneLayout zone)
    {
        _smartCurrentPaths.TryGetValue(zone.Id, out var currentPath);
        var resolvedPath = SmartFolderCatalog.ResolveNavigationPath(zone, currentPath);
        _smartCurrentPaths[zone.Id] = resolvedPath;
        return resolvedPath;
    }

    private void OnZoneFocusToggleRequested(object? sender, EventArgs e)
    {
        if (sender is not ZoneView view)
        {
            return;
        }

        _focusedZoneId = string.Equals(_focusedZoneId, view.ZoneId, StringComparison.Ordinal)
            ? null
            : view.ZoneId;
        ApplyFocusMode();
        RefreshZoneItems();
    }

    private void OnExitFocusModeMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ExitFocusMode();
        e.Handled = true;
    }

    private void ExitFocusMode()
    {
        if (_focusedZoneId is null)
        {
            return;
        }

        _focusedZoneId = null;
        ApplyFocusMode();
        RefreshZoneItems();
    }

    private void ApplyFocusMode()
    {
        var focusedLayout = _focusedZoneId is null
            ? null
            : _snapshot.Zones.FirstOrDefault(zone => zone.Id == _focusedZoneId);
        if (_focusedZoneId is not null && focusedLayout is null)
        {
            _focusedZoneId = null;
        }

        var focusMode = focusedLayout is not null;
        foreach (var view in _zoneViews.Values)
        {
            var isFocused = focusMode && view.ZoneId == focusedLayout!.Id;
            view.ApplyFocusState(focusMode, isFocused);
            if (isFocused)
            {
                System.Windows.Controls.Panel.SetZIndex(view, _nextZIndex++);
            }
        }

        SearchHint.Text = focusMode ? "搜索当前视域" : "搜索所有文件";
        SearchTextBox.ToolTip = focusMode ? "只搜索当前视域中的文件" : "搜索所有分区中的文件";
        FocusModeIndicator.Visibility = focusMode ? Visibility.Visible : Visibility.Collapsed;
        FocusModeText.Text = focusMode ? $"视域 · {focusedLayout!.Name}" : "视域";

        if (focusMode)
        {
            FocusBackdrop.Visibility = Visibility.Visible;
            FocusBackdrop.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }
        else
        {
            FocusBackdrop.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            FocusBackdrop.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnZoneItemDropped(object? sender, ZoneItemDroppedEventArgs e)
    {
        var sourceZone = _snapshot.Zones.FirstOrDefault(zone => zone.Id == e.SourceZoneId);
        var targetZone = _snapshot.Zones.FirstOrDefault(zone => zone.Id == e.ZoneId);
        if (sourceZone is null || targetZone is null || sourceZone.Id == targetZone.Id || e.Paths.Count == 0)
        {
            return;
        }

        if (sourceZone.Kind == ZoneKind.Desktop && targetZone.Kind == ZoneKind.Desktop)
        {
            RecordHistory();
            var changed = false;
            foreach (var path in e.Paths.Where(path => _entries.Any(entry =>
                         string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))))
            {
                changed |= LayoutStateManager.MoveItem(_snapshot, path, targetZone.Id);
            }

            if (!changed)
            {
                _history.Undo();
                UpdateUndoState();
                return;
            }

            RefreshZoneItems();
            SaveSnapshot();
            return;
        }

        if (targetZone.Kind == ZoneKind.SmartFolder)
        {
            var rejected = e.Paths.Where(path => !SmartFolderCatalog.MatchesFilter(targetZone, path)).ToArray();
            if (rejected.Length > 0)
            {
                var formats = targetZone.Extensions.Count == 0
                    ? "文件"
                    : string.Join("、", targetZone.Extensions);
                System.Windows.MessageBox.Show(
                    $"有 {rejected.Length} 个项目不符合目标分区的 {formats} 筛选条件，因此没有移动任何文件。",
                    "格式不符合",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        var targetDirectory = targetZone.Kind == ZoneKind.SmartFolder
            ? GetSmartCurrentPath(targetZone)
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!Directory.Exists(targetDirectory))
        {
            System.Windows.MessageBox.Show("目标文件夹当前不可用。", "无法移动文件", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ConfirmRealFileMove(e.Paths.Count, targetDirectory))
        {
            return;
        }

        var snapshotBeforeMove = _snapshot.Clone();
        var moveResult = _fileOperationService.MoveFiles(e.Paths, targetDirectory);
        if (moveResult.Moves.Count > 0)
        {
            foreach (var move in moveResult.Moves)
            {
                _snapshot.Placements.RemoveAll(placement =>
                    string.Equals(placement.Path, move.SourcePath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(placement.Path, move.DestinationPath, StringComparison.OrdinalIgnoreCase));
                if (targetZone.Kind == ZoneKind.Desktop)
                {
                    LayoutStateManager.MoveItem(_snapshot, move.DestinationPath, targetZone.Id);
                }
            }

            _history.PushFileMove(snapshotBeforeMove, moveResult.Moves);
            UpdateUndoState();
            ShowOperationToast($"已移动 {moveResult.Moves.Count} 个文件到 {targetDirectory}", canUndo: true);
            await RefreshAfterFileOperationAsync();
        }

        if (moveResult.Failures.Count > 0)
        {
            ShowFileOperationFailures("部分文件移动失败", moveResult.Failures);
        }
    }

    private async void OnEditSmartZoneRequested(object? sender, EventArgs e)
    {
        if (sender is not ZoneView view)
        {
            return;
        }

        var index = _snapshot.Zones.FindIndex(zone => zone.Id == view.ZoneId);
        if (index < 0 || _snapshot.Zones[index].Kind != ZoneKind.SmartFolder || _snapshot.Zones[index].IsPinned)
        {
            return;
        }

        var editor = new SmartZoneEditorWindow(_snapshot.Zones[index])
        {
            Owner = _previewMode ? this : null,
            WindowStartupLocation = _previewMode
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
        };
        if (editor.ShowDialog() != true || editor.Result is null)
        {
            return;
        }

        var previousSourcePath = _snapshot.Zones[index].SourcePath;
        RecordHistory();
        _snapshot.Zones[index] = editor.Result.Clone();
        if (!string.Equals(
                previousSourcePath,
                _snapshot.Zones[index].SourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            _smartCurrentPaths.Remove(_snapshot.Zones[index].Id);
        }

        view.ApplyLayout(_snapshot.Zones[index]);
        SaveSnapshot();
        RestartSmartFolderMonitoring();
        await RefreshSmartZoneAsync(_snapshot.Zones[index]);
    }

    private async void OnRenameFileRequested(object? sender, ItemPathEventArgs e)
    {
        if (!File.Exists(e.Path) && !Directory.Exists(e.Path))
        {
            return;
        }

        var prompt = new TextPromptWindow("重命名文件", "输入新的文件名", Path.GetFileName(e.Path))
        {
            Owner = _previewMode ? this : null,
            WindowStartupLocation = _previewMode
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
        };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        var snapshotBeforeRename = _snapshot.Clone();
        var result = _fileOperationService.Rename(e.Path, prompt.Value);
        if (result.Moves.Count > 0)
        {
            var move = result.Moves[0];
            LayoutStateManager.RenameItemPath(_snapshot, move.SourcePath, move.DestinationPath);
            _history.PushFileMove(snapshotBeforeRename, result.Moves);
            UpdateUndoState();
            ShowOperationToast($"已重命名为 {Path.GetFileName(move.DestinationPath)}", canUndo: true);
            await RefreshAfterFileOperationAsync();
        }

        if (result.Failures.Count > 0)
        {
            ShowFileOperationFailures("无法重命名文件", result.Failures);
        }
    }

    private async void OnRecycleFilesRequested(object? sender, FilePathsEventArgs e)
    {
        if (e.Paths.Count > 1)
        {
            var confirmation = System.Windows.MessageBox.Show(
                $"将选中的 {e.Paths.Count} 个项目移入 Windows 回收站？",
                "批量移入回收站",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var failures = _fileOperationService.MoveToRecycleBin(e.Paths);
        var failedPaths = failures.Select(failure => failure.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var succeeded = e.Paths.Where(path => !failedPaths.Contains(path)).ToArray();
        _snapshot.Placements.RemoveAll(placement => succeeded.Contains(
            placement.Path,
            StringComparer.OrdinalIgnoreCase));
        SaveSnapshot();
        if (succeeded.Length > 0)
        {
            ShowOperationToast($"已将 {succeeded.Length} 个项目移入 Windows 回收站", canUndo: false);
            await RefreshAfterFileOperationAsync();
        }

        if (failures.Count > 0)
        {
            ShowFileOperationFailures("部分项目无法移入回收站", failures);
        }
    }

    private void OnZoneDeleteRequested(object? sender, EventArgs e)
    {
        if (sender is not ZoneView view)
        {
            return;
        }

        var layout = _snapshot.Zones.FirstOrDefault(zone => zone.Id == view.ZoneId);
        if (layout is null || layout.IsPinned)
        {
            return;
        }

        var replacementInbox = layout.IsInbox
            ? _snapshot.Zones.FirstOrDefault(zone =>
                zone.Kind == ZoneKind.Desktop && zone.Id != layout.Id)
            : _snapshot.Zones.FirstOrDefault(zone => zone.Kind == ZoneKind.Desktop && zone.IsInbox);
        var message = layout.Kind == ZoneKind.SmartFolder
            ? $"删除智能分区“{layout.Name}”？\n\n仅删除此视图，不会删除“{layout.SourcePath}”中的任何文件。"
            : replacementInbox is null
                ? $"删除桌面分区“{layout.Name}”？\n\n桌面文件不会被删除；软件中将暂时不显示桌面项目。"
                : layout.IsInbox
                    ? $"删除分区“{layout.Name}”？\n\n未归类项目将改由“{replacementInbox.Name}”承接。真实文件不会被删除。"
                    : $"删除分区“{layout.Name}”？\n\n其中的图标将回到“{replacementInbox.Name}”，真实文件不会被删除。";

        var result = System.Windows.MessageBox.Show(
            message,
            "删除分区",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        RecordHistory();
        if (!LayoutStateManager.DeleteZone(_snapshot, layout.Id))
        {
            _history.Undo();
            UpdateUndoState();
            return;
        }

        BuildZoneViews();
        RefreshZoneItems();
        ApplyWallpaperBackdrop();
        SaveSnapshot();
        if (_smartScanCancellations.Remove(layout.Id, out var cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _smartEntries.Remove(layout.Id);
        _smartErrors.Remove(layout.Id);
        _smartCurrentPaths.Remove(layout.Id);
        RestartSmartFolderMonitoring();
    }

    private void OnReturnToInboxRequested(object? sender, ItemPathEventArgs e)
    {
        RecordHistory();
        var inbox = _snapshot.Zones.FirstOrDefault(zone => zone.IsInbox);
        if (inbox is null)
        {
            _history.Undo();
            UpdateUndoState();
            return;
        }

        if (!LayoutStateManager.MoveItem(_snapshot, e.Path, inbox.Id))
        {
            _history.Undo();
            UpdateUndoState();
            return;
        }

        RefreshZoneItems();
        SaveSnapshot();
    }

    private void OnZoneActivated(object? sender, EventArgs e)
    {
        if (sender is ZoneView view)
        {
            System.Windows.Controls.Panel.SetZIndex(view, _nextZIndex++);
        }
    }

    private void RefreshZoneItems()
    {
        var desktopZones = _snapshot.Zones.Where(zone => zone.Kind == ZoneKind.Desktop).ToArray();
        var inbox = desktopZones.FirstOrDefault(zone => zone.IsInbox);
        var validZoneIds = desktopZones.Select(zone => zone.Id).ToHashSet(StringComparer.Ordinal);
        var groups = desktopZones.ToDictionary(
            zone => zone.Id,
            _ => new List<(DesktopEntry Entry, int Order)>(),
            StringComparer.Ordinal);

        if (inbox is not null)
        {
            foreach (var entry in _entries)
            {
                var placement = _snapshot.Placements.LastOrDefault(candidate =>
                    string.Equals(candidate.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                var targetId = placement is not null && validZoneIds.Contains(placement.ZoneId)
                    ? placement.ZoneId
                    : inbox.Id;
                groups[targetId].Add((entry, placement?.OrderIndex ?? int.MaxValue));
            }
        }

        foreach (var zone in _snapshot.Zones)
        {
            if (!_zoneViews.TryGetValue(zone.Id, out var view))
            {
                continue;
            }

            if (zone.Kind == ZoneKind.SmartFolder &&
                _smartErrors.TryGetValue(zone.Id, out var error) &&
                !string.IsNullOrWhiteSpace(error))
            {
                view.SetStatus(error);
                continue;
            }

            var allItems = zone.Kind == ZoneKind.SmartFolder
                ? _smartEntries.GetValueOrDefault(zone.Id, [])
                : groups[zone.Id]
                    .OrderBy(item => item.Order)
                    .ThenBy(item => item.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(item => item.Entry)
                    .ToArray();
            var shouldFilter = _searchQuery.Length > 0 &&
                (_focusedZoneId is null || zone.Id == _focusedZoneId);
            var visibleItems = !shouldFilter
                ? allItems
                : allItems.Where(entry =>
                    entry.Name.Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase) ||
                    entry.RelativeDirectory.Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase) ||
                    entry.Path.Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            view.SetItems(
                visibleItems,
                shouldFilter ? allItems.Count : null);
        }
    }

    private void ClampZonesToWorkspace()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        foreach (var layout in _snapshot.Zones)
        {
            layout.ClampTo(ActualWidth, ActualHeight);
            if (_zoneViews.TryGetValue(layout.Id, out var view))
            {
                view.ApplyLayout(layout);
            }
        }
    }

    private void ApplyWallpaperBackdrop()
    {
        _wallpaper = _wallpaperService.LoadCurrentWallpaper();
        if (_wallpaper is null)
        {
            return;
        }

        foreach (var view in _zoneViews.Values)
        {
            view.SetBackdrop(_wallpaper, new System.Windows.Size(ActualWidth, ActualHeight));
        }

        if (_previewMode)
        {
            Background = new System.Windows.Media.ImageBrush(_wallpaper)
            {
                Stretch = System.Windows.Media.Stretch.UniformToFill,
            };
        }
    }

    private void SaveSnapshot()
    {
        _layoutStore.Save(_snapshot);
    }

    private bool ConfirmRealFileMove(int count, string targetDirectory)
    {
        if (_settings.HasConfirmedRealFileMoves)
        {
            return true;
        }

        var confirmation = System.Windows.MessageBox.Show(
            $"这次拖放会移动 {count} 个真实文件到：\n\n{targetDirectory}\n\n以后跨类型分区拖放都会直接移动文件，并可在当前会话中撤销。是否继续？",
            "确认真实文件移动",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return false;
        }

        _settings.HasConfirmedRealFileMoves = true;
        _settingsStore.Save(_settings);
        return true;
    }

    private async Task RefreshAfterFileOperationAsync()
    {
        _entries = _desktopCatalog.LoadEntries();
        var desktopPaths = _entries.Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _snapshot.Placements.RemoveAll(placement => !desktopPaths.Contains(placement.Path));
        RefreshZoneItems();
        SaveSnapshot();
        await RefreshAllSmartZonesAsync();
    }

    private void ShowOperationToast(string message, bool canUndo)
    {
        OperationToastText.Text = message;
        OperationToastUndoButton.Visibility = canUndo
            ? Visibility.Visible
            : Visibility.Collapsed;
        OperationToast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideOperationToast()
    {
        _toastTimer.Stop();
        OperationToast.Visibility = Visibility.Collapsed;
    }

    private static void ShowFileOperationFailures(
        string title,
        IReadOnlyCollection<FileOperationFailure> failures)
    {
        var details = string.Join(
            "\n",
            failures.Take(6).Select(failure => $"• {Path.GetFileName(failure.Path)}：{failure.Reason}"));
        if (failures.Count > 6)
        {
            details += $"\n…另有 {failures.Count - 6} 项";
        }

        System.Windows.MessageBox.Show(
            $"{failures.Count} 项操作未完成：\n\n{details}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void RecordHistory()
    {
        _history.Push(_snapshot);
        UpdateUndoState();
    }

    private void UpdateUndoState()
    {
        UndoButton.IsEnabled = _history.CanUndo;
    }

    private bool ApplyOrganizationRules()
    {
        var items = _entries
            .Select(entry => new DesktopItemDescriptor(entry.Name, entry.Path))
            .ToArray();
        return LayoutStateManager.ApplyRules(_snapshot, items);
    }

    private void ApplySettingsToWindow()
    {
        CommandToolbar.Visibility = _settings.ShowCommandToolbar
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_previewMode)
        {
            return;
        }

        if (_settings.HideNativeDesktopIcons)
        {
            _desktopIconVisibility.Hide();
        }
        else
        {
            _desktopIconVisibility.Restore();
        }
    }

    private static bool RulesEquivalent(
        IReadOnlyCollection<OrganizationRule> left,
        IReadOnlyCollection<OrganizationRule> right)
    {
        return left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Id == pair.Second.Id &&
            pair.First.Name == pair.Second.Name &&
            pair.First.IsEnabled == pair.Second.IsEnabled &&
            pair.First.Priority == pair.Second.Priority &&
            pair.First.MatchKind == pair.Second.MatchKind &&
            pair.First.Pattern == pair.Second.Pattern &&
            pair.First.TargetZoneId == pair.Second.TargetZoneId);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest)
        {
            return IntPtr.Zero;
        }

        var screenX = unchecked((short)(long)lParam);
        var screenY = unchecked((short)((long)lParam >> 16));
        var point = PointFromScreen(new System.Windows.Point(screenX, screenY));
        var hit = InputHitTest(point) as DependencyObject;

        while (hit is not null)
        {
            if (hit is ZoneView ||
                ReferenceEquals(hit, CommandToolbar) ||
                ReferenceEquals(hit, FocusModeIndicator) ||
                ReferenceEquals(hit, OperationToast))
            {
                return IntPtr.Zero;
            }

            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        }

        handled = true;
        return new IntPtr(HtTransparent);
    }
}

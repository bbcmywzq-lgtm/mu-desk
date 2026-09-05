using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DesktopOrganizer;
using MouseRing;
using PersonalToolbox.Modules;
using Toolbox.Core;
using MediaColor = System.Windows.Media.Color;

namespace PersonalToolbox;

public partial class MainWindow : Window
{
    private readonly MouseRingModule _mouseRing;
    private readonly EffectCaptureModule _effectCapture;
    private readonly CueModule _cue;
    private readonly CursorGalleryModule _cursorGallery;
    private readonly CispQuestionBankModule _cispQuestionBank;
    private readonly DesktopOrganizerModule _desktopOrganizer;
    private readonly FileShelfModule _fileShelf;
    private readonly LightPetModule _lightPet;
    private readonly ReminderNotesModule _reminderNotes;
    private readonly ToolboxSettings _settings;
    private bool _updating;
    private bool _allowClose;

    public MainWindow(
        MouseRingModule mouseRing,
        CueModule cue,
        EffectCaptureModule effectCapture,
        CursorGalleryModule cursorGallery,
        CispQuestionBankModule cispQuestionBank,
        DesktopOrganizerModule desktopOrganizer,
        FileShelfModule fileShelf,
        LightPetModule lightPet,
        ReminderNotesModule reminderNotes,
        ToolboxSettings settings)
    {
        InitializeComponent();
        _mouseRing = mouseRing;
        _cue = cue;
        _effectCapture = effectCapture;
        _cursorGallery = cursorGallery;
        _cispQuestionBank = cispQuestionBank;
        _desktopOrganizer = desktopOrganizer;
        _fileShelf = fileShelf;
        _lightPet = lightPet;
        _reminderNotes = reminderNotes;
        _settings = settings;
        _mouseRing.StateChanged += (_, _) => Dispatcher.Invoke(RefreshState);
        _cue.StateChanged += (_, _) => Dispatcher.Invoke(RefreshState);
        _effectCapture.SettingsChanged += (_, _) => Dispatcher.Invoke(RefreshState);
        _desktopOrganizer.StateChanged += (_, _) => Dispatcher.Invoke(RefreshState);
        _fileShelf.StateChanged += (_, _) => Dispatcher.Invoke(RefreshState);
        _lightPet.StateChanged += (_, _) => Dispatcher.Invoke(RefreshState);
        RefreshState();
    }

    public event Action<bool>? MouseRingEnabledChanged;

    public event Action<bool>? CueEnabledChanged;

    public event Action<bool>? DesktopOrganizerEnabledChanged;

    public event Action<bool>? FileShelfEnabledChanged;

    public event Action<bool>? LightPetEnabledChanged;

    public event Action<bool>? StartupChanged;

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void PrepareForShutdown()
    {
        _allowClose = true;
        Close();
    }

    public void RefreshState()
    {
        _updating = true;
        MouseRingToggle.IsChecked = _settings.MouseRingEnabled;
        CueToggle.IsChecked = _settings.Cue.Enabled;
        DesktopToggle.IsChecked = _settings.DesktopOrganizerEnabled;
        FileShelfToggle.IsChecked = _settings.FileShelf.Enabled;
        LightPetToggle.IsChecked = _settings.LightPetEnabled;
        StartupToggle.IsChecked = _settings.RunAtStartup;
        _updating = false;

        SetStatus(
            MouseRingStatusText,
            MouseRingStatusDot,
            MouseRingStatusPill,
            _settings.MouseRingEnabled && _mouseRing.IsRunning,
            _mouseRing.IsPaused);
        SetStatus(
            CueStatusText,
            CueStatusDot,
            CueStatusPill,
            _settings.Cue.Enabled && _cue.IsRunning,
            _cue.IsPaused);
        SetStatus(
            DesktopStatusText,
            DesktopStatusDot,
            DesktopStatusPill,
            _settings.DesktopOrganizerEnabled && _desktopOrganizer.IsRunning,
            _desktopOrganizer.IsPaused);
        SetStatus(
            FileShelfStatusText,
            FileShelfStatusDot,
            FileShelfStatusPill,
            _settings.FileShelf.Enabled && _fileShelf.IsRunning,
            _fileShelf.IsPaused);
        FileShelfCountText.Text = _fileShelf.PinnedCount > 0
            ? $"{_fileShelf.ItemCount} 项 · {_fileShelf.PinnedCount} 项已固定"
            : $"{_fileShelf.ItemCount} 项";
        FileShelfShowButton.Content = _fileShelf.IsExpanded ? "收起货架" : "显示货架";
        FileShelfShowButton.IsEnabled = _settings.FileShelf.Enabled && _fileShelf.IsRunning && !_fileShelf.IsPaused;
        SetStatus(
            LightPetStatusText,
            LightPetStatusDot,
            LightPetStatusPill,
            _settings.LightPetEnabled && _lightPet.IsRunning,
            paused: false);
        LightPetVisibilityButton.Content = _lightPet.IsVisible ? "隐藏桌宠" : "显示桌宠";
        LightPetVisibilityButton.IsEnabled = _settings.LightPetEnabled && _lightPet.IsRunning;
        LightPetSettingsButton.IsEnabled = _settings.LightPetEnabled && _lightPet.IsRunning;
        EffectCaptureShortcutText.Text = string.IsNullOrWhiteSpace(_settings.EffectCapture.GlobalHotkey)
            ? "快捷键未设置"
            : _settings.EffectCapture.GlobalHotkey.Replace("+", " + ", StringComparison.Ordinal);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private static void SetStatus(TextBlock text, Shape dot, Border pill, bool running, bool paused)
    {
        if (!running)
        {
            text.Text = "已关闭";
            text.Foreground = Brush(103, 111, 124);
            dot.Fill = Brush(142, 149, 160);
            pill.Background = Brush(241, 243, 246);
        }
        else if (paused)
        {
            text.Text = "已暂停";
            text.Foreground = Brush(147, 96, 26);
            dot.Fill = Brush(218, 143, 45);
            pill.Background = Brush(255, 246, 229);
        }
        else
        {
            text.Text = "运行中";
            text.Foreground = Brush(39, 121, 74);
            dot.Fill = Brush(46, 164, 95);
            pill.Background = Brush(234, 246, 238);
        }
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(MediaColor.FromRgb(red, green, blue));

    private void MouseRingToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            MouseRingEnabledChanged?.Invoke(MouseRingToggle.IsChecked == true);
        }
    }

    private void CueToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            CueEnabledChanged?.Invoke(CueToggle.IsChecked == true);
        }
    }

    private void DesktopToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            DesktopOrganizerEnabledChanged?.Invoke(DesktopToggle.IsChecked == true);
        }
    }

    private void FileShelfToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            FileShelfEnabledChanged?.Invoke(FileShelfToggle.IsChecked == true);
        }
    }

    private void LightPetToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            LightPetEnabledChanged?.Invoke(LightPetToggle.IsChecked == true);
        }
    }

    private void StartupToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            StartupChanged?.Invoke(StartupToggle.IsChecked == true);
        }
    }

    private void MouseRingSettings_OnClick(object sender, RoutedEventArgs e) => _mouseRing.OpenSettings();

    private void MouseRingPreview_OnClick(object sender, RoutedEventArgs e) => _mouseRing.ShowPreview();

    private void CueToolbar_OnClick(object sender, RoutedEventArgs e) => _cue.OpenToolbar();

    private void CueSettings_OnClick(object sender, RoutedEventArgs e) => _cue.OpenSettings();

    private void CursorGallery_OnClick(object sender, RoutedEventArgs e) => _cursorGallery.OpenSettings();

    private void CispQuestionBank_OnClick(object sender, RoutedEventArgs e) => _cispQuestionBank.OpenSettings();

    private void CispLatestSets_OnClick(object sender, RoutedEventArgs e) => _cispQuestionBank.OpenLatestSets();

    private void EffectCapture_OnClick(object sender, RoutedEventArgs e) => _effectCapture.StartCapture();

    private void EffectCaptureSettings_OnClick(object sender, RoutedEventArgs e) => _effectCapture.OpenSettings(this);

    private void ShowDesktop_OnClick(object sender, RoutedEventArgs e) => _desktopOrganizer.ShowDesktop();

    private void DesktopSettings_OnClick(object sender, RoutedEventArgs e) => _desktopOrganizer.OpenSettings();

    private void FileShelfShow_OnClick(object sender, RoutedEventArgs e) => _fileShelf.ToggleShelf();

    private void LightPetVisibility_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_lightPet.ToggleVisibility())
        {
            ShowMissingPetMessage();
        }
    }

    private void LightPetSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_lightPet.OpenSettings())
        {
            ShowMissingPetMessage();
        }
    }

    private void ShowMissingPetMessage() => System.Windows.MessageBox.Show(
        this,
        "没有找到 Pal。请重新发布或安装 MU Desk。",
        "MU Desk",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    private void ReminderNotesReminder_OnClick(object sender, RoutedEventArgs e) =>
        OpenReminderNotes(ReminderNotesPage.Reminder);

    private void ReminderNotesNote_OnClick(object sender, RoutedEventArgs e) =>
        OpenReminderNotes(ReminderNotesPage.Note);

    public void OpenReminderNotes(ReminderNotesPage page)
    {
        if (!_reminderNotes.Open(page))
        {
            System.Windows.MessageBox.Show(
                this,
                "没有找到“Memo”模块。请重新发布或安装 MU Desk。",
                "MU Desk",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}

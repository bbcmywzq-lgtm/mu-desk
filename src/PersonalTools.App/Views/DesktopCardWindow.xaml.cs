using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PersonalTools.Core;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace PersonalTools.App.Views;

public partial class DesktopCardWindow : Window
{
    private const double SnapDistance = 11;
    private const double CardGap = 7;
    private readonly IEntryProvider _provider;
    private readonly Func<string, IReadOnlyList<Rect>> _snapTargets;
    private readonly DispatcherTimer _placementSaveTimer;
    private readonly DispatcherTimer _contentSaveTimer;
    private EntryItem _entry;
    private bool _applying;
    private bool _snapping;
    private bool _allowClose;

    public DesktopCardWindow(
        IEntryProvider provider,
        EntryItem entry,
        Func<string, IReadOnlyList<Rect>> snapTargets)
    {
        _provider = provider;
        _entry = entry;
        _snapTargets = snapTargets;
        InitializeComponent();

        _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _placementSaveTimer.Tick += OnPlacementSaveTimerTick;
        _contentSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _contentSaveTimer.Tick += OnContentSaveTimerTick;

        LocationChanged += (_, _) => OnPlacementChanged();
        SizeChanged += (_, _) => SchedulePlacementSave();
        ApplyEntry();
    }

    public string EntryId => _entry.Id;

    public Rect Bounds => new(
        Left,
        Top,
        ActualWidth > 0 ? ActualWidth : Width,
        ActualHeight > 0 ? ActualHeight : Height);

    public event EventHandler? Changed;

    public event EventHandler<string>? OpenRequested;

    public void UpdateEntry(EntryItem entry)
    {
        _entry = entry;
        ApplyEntry(preserveFocusedEditor: true);
    }

    public void CloseWithoutUnpin()
    {
        _allowClose = true;
        _placementSaveTimer.Stop();
        _contentSaveTimer.Stop();
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            _ = UnpinAsync();
        }
        base.OnClosing(e);
    }

    private void ApplyEntry(bool preserveFocusedEditor = false)
    {
        _applying = true;
        var card = _entry.DesktopCard;
        Left = card.Left;
        Top = card.Top;
        Width = card.Width;
        Height = card.IsCollapsed ? 58 : card.Height;
        Opacity = card.Opacity;
        Topmost = card.AlwaysOnTop;
        ResizeMode = card.IsLocked || card.IsCollapsed ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;

        if (!preserveFocusedEditor || !ContentEditor.IsKeyboardFocusWithin)
        {
            ContentEditor.Text = _entry.Content;
        }
        TagsText.Text = _entry.Tags.Count == 0
            ? FirstLine(_entry.Content)
            : string.Join(" · ", _entry.Tags.Select(tag => $"#{tag}"));
        DueText.Text = FormatDue(_entry);
        CompleteButton.Visibility = _entry.Status == EntryStatus.Archived ? Visibility.Collapsed : Visibility.Visible;
        CompleteButton.Content = _entry.Status == EntryStatus.Completed ? "↶" : "✓";
        CompleteButton.ToolTip = _entry.Status == EntryStatus.Completed ? "恢复" : "完成";
        CompleteMenu.Header = _entry.Status == EntryStatus.Completed ? "恢复为未完成" : "标记完成";
        CompleteMenu.Visibility = _entry.Status == EntryStatus.Archived ? Visibility.Collapsed : Visibility.Visible;
        SnoozeMenu.Visibility = _entry.Status == EntryStatus.Active && _entry.DueAt is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        LockMenu.IsChecked = card.IsLocked;
        TopMenu.IsChecked = card.AlwaysOnTop;
        CollapseMenu.Header = card.IsCollapsed ? "展开" : "折叠";
        ApplyColor(card.Color);

        ContentEditor.Visibility = card.IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        MetadataPanel.Visibility = card.IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SaveStateText.Text = "已保存";
        _applying = false;
    }

    private void ApplyColor(string color)
    {
        var (background, border) = color switch
        {
            "violet" => ("#F3ECFF", "#CDB8E8"),
            "mint" => ("#E9F8EE", "#ADD5B9"),
            "rose" => ("#FFF0F2", "#E3B9C1"),
            "blue" => ("#EAF4FF", "#B3CEE8"),
            _ => ("#FFF8D9", "#CBBE86"),
        };
        CardBorder.Background = Brush(background);
        CardBorder.BorderBrush = Brush(border);
    }

    private void OnCardMouseEnter(object sender, MouseEventArgs e) =>
        HoverToolbar.Visibility = Visibility.Visible;

    private void OnCardMouseLeave(object sender, MouseEventArgs e)
    {
        if (!ContentEditor.IsKeyboardFocusWithin && CardMenu.IsOpen == false)
        {
            HoverToolbar.Visibility = Visibility.Collapsed;
        }
    }

    private void OnContentFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HoverToolbar.Visibility = Visibility.Visible;
        SaveStateText.Text = "自动保存";
    }

    private async void OnContentLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await SaveContentAsync();
        if (!IsMouseOver)
        {
            HoverToolbar.Visibility = Visibility.Collapsed;
        }
    }

    private void OnContentTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_applying)
        {
            return;
        }
        _contentSaveTimer.Stop();
        if (string.IsNullOrWhiteSpace(ContentEditor.Text))
        {
            SaveStateText.Text = "内容不能为空";
            SaveStateText.Foreground = Brushes.Firebrick;
            return;
        }
        SaveStateText.Foreground = Brush("#8B8192");
        SaveStateText.Text = "正在输入…";
        _contentSaveTimer.Start();
    }

    private async void OnContentSaveTimerTick(object? sender, EventArgs e)
    {
        _contentSaveTimer.Stop();
        await SaveContentAsync();
    }

    private async Task SaveContentAsync()
    {
        _contentSaveTimer.Stop();
        var content = ContentEditor.Text.Trim();
        if (content.Length == 0 || content == _entry.Content)
        {
            SaveStateText.Text = content.Length == 0 ? "内容不能为空" : "已保存";
            return;
        }
        try
        {
            SaveStateText.Text = "保存中…";
            _entry = await _provider.UpdateAsync(_entry.Id, new UpdateEntryCommand(content));
            SaveStateText.Text = "已保存";
            TagsText.Text = _entry.Tags.Count == 0
                ? FirstLine(_entry.Content)
                : string.Join(" · ", _entry.Tags.Select(tag => $"#{tag}"));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            SaveStateText.Foreground = Brushes.Firebrick;
            SaveStateText.Text = "保存失败";
        }
    }

    private async void OnContentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await SaveContentAsync();
        }
    }

    private void OnPlacementChanged()
    {
        if (!_applying && !_entry.DesktopCard.IsLocked)
        {
            SnapToNearbyEdges();
        }
        SchedulePlacementSave();
    }

    private void SchedulePlacementSave()
    {
        if (_applying || !_entry.DesktopCard.IsPinned)
        {
            return;
        }
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private async void OnPlacementSaveTimerTick(object? sender, EventArgs e)
    {
        _placementSaveTimer.Stop();
        var screen = CurrentScreen();
        var card = _entry.DesktopCard with
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = _entry.DesktopCard.IsCollapsed ? _entry.DesktopCard.Height : Height,
            MonitorId = screen?.DeviceName,
        };
        try
        {
            _entry = await _provider.SetDesktopCardAsync(_entry.Id, card);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Placement persistence must not tear down a visible card.
        }
    }

    private void SnapToNearbyEdges()
    {
        if (_snapping || double.IsNaN(Left) || double.IsNaN(Top))
        {
            return;
        }

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var area = CurrentWorkingArea();
        var xCandidates = new List<double> { area.Left, area.Right - width };
        var yCandidates = new List<double> { area.Top, area.Bottom - height };
        foreach (var target in _snapTargets(_entry.Id))
        {
            xCandidates.Add(target.Left);
            xCandidates.Add(target.Right - width);
            xCandidates.Add(target.Left - width - CardGap);
            xCandidates.Add(target.Right + CardGap);
            yCandidates.Add(target.Top);
            yCandidates.Add(target.Bottom - height);
            yCandidates.Add(target.Top - height - CardGap);
            yCandidates.Add(target.Bottom + CardGap);
        }

        var snappedLeft = ClosestSnap(Left, xCandidates);
        var snappedTop = ClosestSnap(Top, yCandidates);
        if (snappedLeft == Left && snappedTop == Top)
        {
            return;
        }

        _snapping = true;
        Left = snappedLeft;
        Top = snappedTop;
        _snapping = false;
    }

    private static double ClosestSnap(double value, IEnumerable<double> candidates)
    {
        var best = value;
        var bestDistance = SnapDistance + 1;
        foreach (var candidate in candidates)
        {
            var distance = Math.Abs(candidate - value);
            if (distance <= SnapDistance && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    private Rect CurrentWorkingArea()
    {
        var screen = CurrentScreen() ?? System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null)
        {
            return SystemParameters.WorkArea;
        }
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private System.Windows.Forms.Screen? CurrentScreen()
    {
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var devicePoint = transform.Transform(new Point(Left + Width / 2, Top + Height / 2));
        return System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)devicePoint.X, (int)devicePoint.Y));
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _ = ToggleCollapsedAsync();
            return;
        }
        if (!_entry.DesktopCard.IsLocked && e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
                SnapToNearbyEdges();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        CardMenu.PlacementTarget = sender as Button;
        CardMenu.IsOpen = true;
    }

    private async void OnCompleteClick(object sender, RoutedEventArgs e)
    {
        await SaveContentAsync();
        _entry = _entry.Status == EntryStatus.Completed
            ? await _provider.SetStatusAsync(_entry.Id, EntryStatus.Active)
            : await _provider.CompleteAsync(_entry.Id);
        ApplyEntry();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async void OnSnoozeClick(object sender, RoutedEventArgs e)
    {
        _entry = await _provider.SnoozeAsync(_entry.Id, DateTimeOffset.UtcNow.AddMinutes(10));
        ApplyEntry();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        await SaveContentAsync();
        OpenRequested?.Invoke(this, _entry.Id);
    }

    private async void OnLockClick(object sender, RoutedEventArgs e)
    {
        _entry = await _provider.SetDesktopCardAsync(
            _entry.Id,
            _entry.DesktopCard with { IsLocked = !_entry.DesktopCard.IsLocked });
        ApplyEntry();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async void OnTopClick(object sender, RoutedEventArgs e)
    {
        _entry = await _provider.SetDesktopCardAsync(
            _entry.Id,
            _entry.DesktopCard with { AlwaysOnTop = !_entry.DesktopCard.AlwaysOnTop });
        ApplyEntry();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnUnpinClick(object sender, RoutedEventArgs e) => _ = UnpinAsync();

    private async void OnColorMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string color })
        {
            return;
        }
        _entry = await _provider.SetDesktopCardAsync(
            _entry.Id,
            _entry.DesktopCard with { Color = color });
        ApplyEntry();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async void OnOpacityMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } ||
            !double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var opacity))
        {
            return;
        }
        _entry = await _provider.SetDesktopCardAsync(
            _entry.Id,
            _entry.DesktopCard with { Opacity = opacity });
        ApplyEntry();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCollapseMenuClick(object sender, RoutedEventArgs e) => _ = ToggleCollapsedAsync();

    private async Task UnpinAsync()
    {
        if (!_entry.DesktopCard.IsPinned)
        {
            return;
        }
        await SaveContentAsync();
        _entry = await _provider.SetDesktopCardAsync(
            _entry.Id,
            _entry.DesktopCard with { IsPinned = false });
        Changed?.Invoke(this, EventArgs.Empty);
        _allowClose = true;
        Close();
    }

    private async Task ToggleCollapsedAsync()
    {
        await SaveContentAsync();
        _entry = await _provider.SetDesktopCardAsync(
            _entry.Id,
            _entry.DesktopCard with { IsCollapsed = !_entry.DesktopCard.IsCollapsed });
        ApplyEntry();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string FirstLine(string content)
    {
        var value = content.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();
        return value.Length <= 30 ? value : value[..30] + "…";
    }

    private static string FormatDue(EntryItem entry)
    {
        if (entry.Status == EntryStatus.Completed)
        {
            return "已完成 · " + FirstLine(entry.Content);
        }
        if (entry.DueAt is not { } dueAt)
        {
            return FirstLine(entry.Content);
        }
        var local = dueAt.ToLocalTime();
        var prefix = local.Date == DateTime.Today
            ? $"今天 {local:HH:mm}"
            : local.Date == DateTime.Today.AddDays(1)
                ? $"明天 {local:HH:mm}"
                : local.ToString("M月d日 HH:mm");
        return $"{prefix} · {FirstLine(entry.Content)}";
    }

    private static SolidColorBrush Brush(string value) =>
        new((Color)ColorConverter.ConvertFromString(value));
}

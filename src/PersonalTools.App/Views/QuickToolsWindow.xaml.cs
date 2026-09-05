using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PersonalTools.App.Services;
using PersonalTools.Core;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace PersonalTools.App.Views;

public partial class QuickToolsWindow : Window
{
    private readonly IEntryProvider _provider;
    private readonly DesktopCardManager _desktopCards;
    private readonly CancellationTokenSource _closingCancellation = new();
    private readonly DispatcherTimer _editorSaveTimer;
    private string _currentFilter = "全部";
    private string? _editingId;
    private EntryItem? _selectedEntry;
    private bool _draftActive;
    private bool _draftPin;
    private bool _draftFavorite;
    private string _draftColor = "violet";
    private bool _contentDirty;
    private bool _tagsDirty;
    private bool _loadingEditor;
    private bool _refreshingList;
    private bool _busy;

    public QuickToolsWindow(IEntryProvider provider, DesktopCardManager desktopCards)
    {
        _provider = provider;
        _desktopCards = desktopCards;
        InitializeComponent();
        _editorSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _editorSaveTimer.Tick += OnEditorSaveTimerTick;
        SetDueTime(DateTimeOffset.Now.AddMinutes(30));
        Loaded += async (_, _) =>
        {
            UpdateFilterButtons();
            await RefreshAsync();
        };
        Closed += (_, _) => _closingCancellation.Cancel();
        _desktopCards.Changed += (_, _) => Dispatcher.BeginInvoke(async () =>
        {
            UpdateDesktopControls();
            await RefreshAsync();
        });
    }

    public event EventHandler? EntrySaved;

    public void ShowReminderPage()
    {
        ShowAndActivate();
        BeginDraft(reminder: true);
    }

    public void ShowNotePage()
    {
        ShowAndActivate();
        BeginDraft(reminder: false);
    }

    public async Task EditEntryAsync(string id)
    {
        var entry = await _provider.GetAsync(id, _closingCancellation.Token);
        if (entry is null)
        {
            return;
        }
        ShowAndActivate();
        LoadEditor(entry);
        SelectListRow(entry.Id);
    }

    public async Task RefreshAsync()
    {
        if (_closingCancellation.IsCancellationRequested)
        {
            return;
        }
        try
        {
            var includeArchived = _currentFilter == "已归档";
            var entries = await _provider.QueryAsync(
                new EntryQuery(
                    Text: SearchBox.Text.Trim(),
                    IncludeCompleted: true,
                    IncludeArchived: includeArchived,
                    Limit: 500),
                _closingCancellation.Token);
            var filtered = ApplyFilter(entries).ToArray();
            var rows = filtered.Select(ToRow).ToArray();

            _refreshingList = true;
            EntryList.ItemsSource = rows;
            if (!_draftActive && _editingId is { } selectedId)
            {
                EntryList.SelectedItem = rows.FirstOrDefault(row => row.Id == selectedId);
            }
            _refreshingList = false;

            CountText.Text = $"{rows.Length} 条";
            EmptyListText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            DesktopCountText.Text = $"{entries.Count(item => item.DesktopCard.IsPinned && item.Status != EntryStatus.Archived)} 张";
            UpdateDesktopControls();

            if (!_draftActive && !_busy && !_contentDirty && !_tagsDirty && _editingId is { } openId)
            {
                var refreshed = entries.FirstOrDefault(item => item.Id == openId);
                if (refreshed is not null && refreshed.Revision != _selectedEntry?.Revision)
                {
                    LoadEditor(refreshed);
                }
            }

            if (!_draftActive && _editingId is null && rows.Length > 0)
            {
                await SelectAndLoadAsync(rows[0]);
            }
            else if (!_draftActive && _editingId is not null && rows.All(row => row.Id != _editingId))
            {
                ClearEditor();
            }
        }
        catch (OperationCanceledException) when (_closingCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetGlobalStatus($"读取失败：{exception.Message}", isError: true);
        }
    }

    private void BeginDraft(bool reminder)
    {
        _editorSaveTimer.Stop();
        _draftActive = true;
        _draftPin = false;
        _draftFavorite = false;
        _draftColor = "violet";
        _editingId = null;
        _selectedEntry = null;
        _contentDirty = false;
        _tagsDirty = false;
        _loadingEditor = true;
        _refreshingList = true;
        EntryList.SelectedItem = null;
        _refreshingList = false;
        EditorPlaceholder.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        EditorHeadingText.Text = "新随记";
        EntryContentBox.Clear();
        TagsBox.Clear();
        ReminderToggle.IsChecked = reminder;
        ReminderPanel.Visibility = reminder ? Visibility.Visible : Visibility.Collapsed;
        RepeatBox.SelectedIndex = 0;
        SetDueTime(DateTimeOffset.Now.AddMinutes(30));
        _loadingEditor = false;
        UpdateEditorButtons();
        EditorStatusText.Foreground = Brush("#817789");
        EditorStatusText.Text = "输入后自动保存";
        EntryContentBox.Focus();
    }

    private void LoadEditor(EntryItem entry)
    {
        _editorSaveTimer.Stop();
        _draftActive = false;
        _editingId = entry.Id;
        _selectedEntry = entry;
        _contentDirty = false;
        _tagsDirty = false;
        _loadingEditor = true;
        EditorPlaceholder.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        EditorHeadingText.Text = FirstLine(entry.Content);
        EntryContentBox.Text = entry.Content;
        TagsBox.Text = string.Join(", ", entry.Tags);
        ReminderToggle.IsChecked = entry.DueAt is not null;
        ReminderPanel.Visibility = Visibility.Collapsed;
        if (entry.DueAt is { } dueAt)
        {
            SetDueTime(dueAt);
        }
        else
        {
            SetDueTime(DateTimeOffset.Now.AddMinutes(30));
        }
        RepeatBox.SelectedIndex = entry.Repeat switch
        {
            RepeatRule.Daily => 1,
            RepeatRule.Weekdays => 2,
            RepeatRule.Weekly => 3,
            _ => 0,
        };
        _loadingEditor = false;
        UpdateEditorButtons();
        EditorStatusText.Foreground = Brush("#817789");
        EditorStatusText.Text = $"修改于 {entry.UpdatedAt.ToLocalTime():M月d日 HH:mm} · 自动保存";
    }

    private void ClearEditor()
    {
        _editorSaveTimer.Stop();
        _draftActive = false;
        _editingId = null;
        _selectedEntry = null;
        _contentDirty = false;
        _tagsDirty = false;
        EditorPanel.Visibility = Visibility.Collapsed;
        EditorPlaceholder.Visibility = Visibility.Visible;
    }

    private async void OnNewNoteClick(object sender, RoutedEventArgs e) =>
        await PrepareNewDraftAsync(reminder: false);

    private async void OnNewReminderClick(object sender, RoutedEventArgs e) =>
        await PrepareNewDraftAsync(reminder: true);

    private async Task PrepareNewDraftAsync(bool reminder)
    {
        await SaveEditorContentAsync();
        BeginDraft(reminder);
    }

    private async void OnEntrySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingList || EntryList.SelectedItem is not EntryRow row)
        {
            return;
        }
        await SaveEditorContentAsync();
        await SelectAndLoadAsync(row);
    }

    private async Task SelectAndLoadAsync(EntryRow row)
    {
        var entry = await _provider.GetAsync(row.Id, _closingCancellation.Token);
        if (entry is not null)
        {
            LoadEditor(entry);
            SelectListRow(entry.Id);
        }
    }

    private void SelectListRow(string id)
    {
        if (EntryList.ItemsSource is not IEnumerable<EntryRow> rows)
        {
            return;
        }
        _refreshingList = true;
        EntryList.SelectedItem = rows.FirstOrDefault(row => row.Id == id);
        EntryList.ScrollIntoView(EntryList.SelectedItem);
        _refreshingList = false;
    }

    private void OnEditorContentChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingEditor)
        {
            return;
        }
        _contentDirty = true;
        EditorHeadingText.Text = string.IsNullOrWhiteSpace(EntryContentBox.Text)
            ? "新随记"
            : FirstLine(EntryContentBox.Text);
        _editorSaveTimer.Stop();
        if (string.IsNullOrWhiteSpace(EntryContentBox.Text))
        {
            EditorStatusText.Foreground = Brush("#817789");
            EditorStatusText.Text = "输入内容后自动保存";
            return;
        }
        EditorStatusText.Foreground = Brush("#817789");
        EditorStatusText.Text = "正在输入…";
        _editorSaveTimer.Start();
    }

    private async void OnEditorSaveTimerTick(object? sender, EventArgs e)
    {
        _editorSaveTimer.Stop();
        await SaveEditorContentAsync();
    }

    private async void OnContentKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.S || e.Key == Key.Enter) && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await SaveEditorContentAsync();
        }
    }

    private async void OnTagsLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        await SaveEditorContentAsync();

    private void OnTagsChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingEditor)
        {
            _tagsDirty = true;
        }
    }

    private async Task SaveEditorContentAsync()
    {
        _editorSaveTimer.Stop();
        if (_loadingEditor || _busy || EditorPanel.Visibility != Visibility.Visible)
        {
            return;
        }
        var content = EntryContentBox.Text.Trim();
        if (content.Length == 0)
        {
            return;
        }
        if (_editingId is not null && !_contentDirty && !_tagsDirty)
        {
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            EditorStatusText.Foreground = Brush("#817789");
            EditorStatusText.Text = "保存中…";
            EntryItem entry;
            if (_editingId is { } id)
            {
                entry = await _provider.UpdateAsync(
                    id,
                    new UpdateEntryCommand(content, ParseTags()),
                    cancellationToken);
            }
            else
            {
                DateTimeOffset? dueAt = null;
                if (ReminderToggle.IsChecked == true &&
                    TryGetDueTime(out var parsed, out _) &&
                    parsed > DateTimeOffset.Now)
                {
                    dueAt = parsed;
                }
                entry = await _provider.CreateAsync(
                    new CreateEntryCommand(
                        content,
                        ParseTags(),
                        dueAt,
                        Repeat: dueAt is null ? RepeatRule.None : CurrentRepeat()),
                    cancellationToken);
                if (_draftFavorite)
                {
                    entry = await _provider.UpdateAsync(
                        entry.Id,
                        new UpdateEntryCommand(entry.Content, IsFavorite: true),
                        cancellationToken);
                }
                if (_draftPin)
                {
                    entry = await SetPinnedAsync(entry, pinned: true, cancellationToken);
                }
                if (_draftColor != entry.DesktopCard.Color)
                {
                    entry = await _provider.SetDesktopCardAsync(
                        entry.Id,
                        entry.DesktopCard with { Color = _draftColor },
                        cancellationToken);
                }
                _draftActive = false;
                _editingId = entry.Id;
                EntrySaved?.Invoke(this, EventArgs.Empty);
            }

            _selectedEntry = entry;
            _contentDirty = false;
            _tagsDirty = false;
            EditorStatusText.Text = "已保存";
            await _desktopCards.SynchronizeAsync(cancellationToken);
            await RefreshAsync();
            UpdateEditorButtons();
        });
    }

    private async void OnReminderToggleChanged(object sender, RoutedEventArgs e)
    {
        if (ReminderPanel is null || ReminderSummaryText is null)
        {
            return;
        }
        UpdateReminderSummary();
        if (_loadingEditor || ReminderToggle.IsChecked == true || _editingId is null)
        {
            return;
        }

        ReminderPanel.Visibility = Visibility.Collapsed;
        await SaveEditorContentAsync();
        await RunBusyAsync(async cancellationToken =>
        {
            var entry = await _provider.UpdateAsync(
                _editingId,
                new UpdateEntryCommand(EntryContentBox.Text.Trim(), ParseTags(), ClearDueAt: true),
                cancellationToken);
            _selectedEntry = entry;
            EditorStatusText.Text = "提醒已移除";
            await _desktopCards.SynchronizeAsync(cancellationToken);
            await RefreshAsync();
            UpdateEditorButtons();
        });
    }

    private void OnReminderEditClick(object sender, RoutedEventArgs e)
    {
        if (ReminderToggle.IsChecked != true)
        {
            _loadingEditor = true;
            ReminderToggle.IsChecked = true;
            SetDueTime(DateTimeOffset.Now.AddMinutes(30));
            RepeatBox.SelectedIndex = 0;
            _loadingEditor = false;
        }
        ReminderPanel.Visibility = ReminderPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateReminderSummary();
    }

    private void OnRemoveReminderClick(object sender, RoutedEventArgs e)
    {
        ReminderPanel.Visibility = Visibility.Collapsed;
        ReminderToggle.IsChecked = false;
        UpdateReminderSummary();
    }

    private async void OnApplyReminderClick(object sender, RoutedEventArgs e) => await SaveReminderAsync();

    private async Task SaveReminderAsync()
    {
        if (ReminderToggle.IsChecked != true)
        {
            return;
        }
        if (!TryGetDueTime(out var dueAt, out var error))
        {
            SetEditorStatus(error, isError: true);
            DueTimeBox.Focus();
            return;
        }
        if (dueAt <= DateTimeOffset.Now)
        {
            SetEditorStatus("提醒时间需要晚于现在。", isError: true);
            DueTimeBox.Focus();
            return;
        }

        await SaveEditorContentAsync();
        if (_editingId is null)
        {
            SetEditorStatus("先写下提醒内容。", isError: true);
            EntryContentBox.Focus();
            return;
        }
        await RunBusyAsync(async cancellationToken =>
        {
            var entry = await _provider.UpdateAsync(
                _editingId,
                new UpdateEntryCommand(
                    EntryContentBox.Text.Trim(),
                    ParseTags(),
                    dueAt,
                    Repeat: CurrentRepeat()),
                cancellationToken);
            _selectedEntry = entry;
            SetEditorStatus($"已设置为 {FormatDue(dueAt)}", isError: false);
            ReminderPanel.Visibility = Visibility.Collapsed;
            await _desktopCards.SynchronizeAsync(cancellationToken);
            await RefreshAsync();
            UpdateEditorButtons();
        });
    }

    private async void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset })
        {
            return;
        }
        var target = preset == "tomorrow9"
            ? LocalDateTimeOffset(DateTime.Today.AddDays(1).AddHours(9))
            : DateTimeOffset.Now.AddMinutes(int.Parse(preset, CultureInfo.InvariantCulture));
        SetDueTime(target);
        if (_editingId is not null)
        {
            await SaveReminderAsync();
        }
    }

    private async void OnPinClick(object sender, RoutedEventArgs e)
    {
        if (_editingId is null)
        {
            _draftPin = !_draftPin;
            UpdateEditorButtons();
            return;
        }
        if (_selectedEntry is null)
        {
            return;
        }
        await RunBusyAsync(async cancellationToken =>
        {
            _selectedEntry = await SetPinnedAsync(
                _selectedEntry,
                !_selectedEntry.DesktopCard.IsPinned,
                cancellationToken);
            await _desktopCards.SynchronizeAsync(cancellationToken);
            await RefreshAsync();
            UpdateEditorButtons();
        });
    }

    private async Task<EntryItem> SetPinnedAsync(
        EntryItem entry,
        bool pinned,
        CancellationToken cancellationToken)
    {
        var state = entry.DesktopCard with { IsPinned = pinned };
        if (pinned && !entry.DesktopCard.IsPinned)
        {
            var pinnedEntries = await _provider.QueryAsync(
                new EntryQuery(PinnedToDesktop: true, IncludeArchived: false),
                cancellationToken);
            var offset = (pinnedEntries.Count % 9) * 22;
            state = state with { Left = 64 + offset, Top = 64 + offset };
        }
        return await _provider.SetDesktopCardAsync(entry.Id, state, cancellationToken);
    }

    private async void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (_editingId is null)
        {
            _draftFavorite = !_draftFavorite;
            UpdateEditorButtons();
            return;
        }
        if (_selectedEntry is null)
        {
            return;
        }
        await RunBusyAsync(async cancellationToken =>
        {
            _selectedEntry = await _provider.UpdateAsync(
                _selectedEntry.Id,
                new UpdateEntryCommand(_selectedEntry.Content, IsFavorite: !_selectedEntry.IsFavorite),
                cancellationToken);
            await RefreshAsync();
            UpdateEditorButtons();
        });
    }

    private async void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color })
        {
            return;
        }
        if (_editingId is null)
        {
            _draftColor = color;
            SetEditorStatus("颜色会在随记保存后应用。", isError: false);
            return;
        }
        if (_selectedEntry is null)
        {
            return;
        }
        await RunBusyAsync(async cancellationToken =>
        {
            _selectedEntry = await _provider.SetDesktopCardAsync(
                _selectedEntry.Id,
                _selectedEntry.DesktopCard with { Color = color },
                cancellationToken);
            await _desktopCards.SynchronizeAsync(cancellationToken);
            await RefreshAsync();
        });
    }

    private async void OnCompleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
        {
            return;
        }
        await SaveEditorContentAsync();
        await RunBusyAsync(async cancellationToken =>
        {
            _selectedEntry = _selectedEntry.Status == EntryStatus.Completed
                ? await _provider.SetStatusAsync(_selectedEntry.Id, EntryStatus.Active, cancellationToken)
                : await _provider.CompleteAsync(_selectedEntry.Id, cancellationToken);
            await _desktopCards.SynchronizeAsync(cancellationToken);
            await RefreshAsync();
            LoadEditor(_selectedEntry);
        });
    }

    private async void OnArchiveClick(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
        {
            return;
        }
        await SaveEditorContentAsync();
        await RunBusyAsync(async cancellationToken =>
        {
            var next = _selectedEntry.Status == EntryStatus.Archived ? EntryStatus.Active : EntryStatus.Archived;
            var entry = await _provider.SetStatusAsync(_selectedEntry.Id, next, cancellationToken);
            if (next == EntryStatus.Archived && entry.DesktopCard.IsPinned)
            {
                entry = await _provider.SetDesktopCardAsync(
                    entry.Id,
                    entry.DesktopCard with { IsPinned = false },
                    cancellationToken);
            }
            _selectedEntry = entry;
            await _desktopCards.SynchronizeAsync(cancellationToken);
            await RefreshAsync();
            if (_currentFilter == "已归档" || next == EntryStatus.Active)
            {
                LoadEditor(entry);
            }
        });
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null || MessageBox.Show(
                this,
                "永久删除这条随记？",
                "Memo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        var id = _selectedEntry.Id;
        await RunBusyAsync(async cancellationToken =>
        {
            await _provider.DeleteAsync(id, cancellationToken);
            _editingId = null;
            _selectedEntry = null;
            await _desktopCards.SynchronizeAsync(cancellationToken);
            await RefreshAsync();
        });
    }

    private async void OnFilterButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filter })
        {
            return;
        }
        await SaveEditorContentAsync();
        _currentFilter = filter;
        ListTitleText.Text = filter == "全部" ? "全部随记" : filter;
        UpdateFilterButtons();
        await RefreshAsync();
    }

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (EntryList is not null)
        {
            await RefreshAsync();
        }
    }

    private void UpdateFilterButtons()
    {
        if (FilterButtonsPanel is null)
        {
            return;
        }
        foreach (var button in FilterButtonsPanel.Children.OfType<Button>())
        {
            var active = string.Equals(button.Tag as string, _currentFilter, StringComparison.Ordinal);
            button.Background = active ? Brush("#EAE0F4") : Brushes.Transparent;
            button.BorderBrush = active ? Brush("#D3C0E5") : Brushes.Transparent;
            button.Foreground = active ? Brush("#684391") : Brush("#4D4258");
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private async void OnArrangeCardsClick(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            await _desktopCards.ArrangeAsync(cancellationToken);
            SetGlobalStatus("桌面便签已自动排列。", isError: false);
            await RefreshAsync();
        });
    }

    private async void OnRescueCardsClick(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            await _desktopCards.RescueOffscreenAsync(cancellationToken);
            SetGlobalStatus("屏幕外的便签已移回主屏幕。", isError: false);
            await RefreshAsync();
        });
    }

    private void OnToggleCardsClick(object sender, RoutedEventArgs e)
    {
        _desktopCards.ToggleVisibility();
        UpdateDesktopControls();
        SetGlobalStatus(_desktopCards.AreCardsHidden ? "桌面便签已暂时隐藏。" : "桌面便签已显示。", isError: false);
    }

    private void UpdateDesktopControls()
    {
        if (ToggleCardsButton is null)
        {
            return;
        }
        ToggleCardsButton.Content = _desktopCards.AreCardsHidden ? "显示全部" : "隐藏全部";
    }

    private void UpdateEditorButtons()
    {
        if (PinButton is null)
        {
            return;
        }
        var pinned = _selectedEntry?.DesktopCard.IsPinned ?? _draftPin;
        var favorite = _selectedEntry?.IsFavorite ?? _draftFavorite;
        PinButton.Content = pinned ? "从桌面收起" : "贴到桌面";
        FavoriteButton.Content = favorite ? "★ 已收藏" : "☆ 收藏";
        var completionApplies = _selectedEntry is not null &&
            (_selectedEntry.DueAt is not null || _selectedEntry.Status == EntryStatus.Completed);
        CompleteActionButton.Visibility = completionApplies ? Visibility.Visible : Visibility.Collapsed;
        CompleteActionButton.IsEnabled = completionApplies;
        ArchiveActionButton.IsEnabled = _selectedEntry is not null;
        DeleteButton.IsEnabled = _selectedEntry is not null;
        CompleteActionButton.Content = _selectedEntry?.Status == EntryStatus.Completed ? "恢复" : "标记完成";
        ArchiveActionButton.Content = _selectedEntry?.Status == EntryStatus.Archived ? "恢复" : "归档";
        UpdateReminderSummary();
    }

    private void UpdateReminderSummary()
    {
        if (ReminderSummaryText is null)
        {
            return;
        }
        var dueAt = _selectedEntry?.DueAt;
        var hasSavedReminder = dueAt is not null;
        if (dueAt is { } savedDueAt)
        {
            ReminderSummaryText.Text = FormatDue(savedDueAt) + FormatRepeatSuffix(_selectedEntry!.Repeat);
        }
        else if (ReminderToggle.IsChecked == true)
        {
            ReminderSummaryText.Text = "正在设置，应用后生效";
        }
        else
        {
            ReminderSummaryText.Text = "未设置";
        }
        ReminderEditButton.Content = ReminderPanel.Visibility == Visibility.Visible
            ? "收起"
            : hasSavedReminder ? "修改" : "添加";
        RemoveReminderButton.Visibility = hasSavedReminder || ReminderToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private IEnumerable<EntryItem> ApplyFilter(IEnumerable<EntryItem> entries) => _currentFilter switch
    {
        "今天" => entries.Where(item => item.Status == EntryStatus.Active && item.DueAt?.ToLocalTime().Date == DateTime.Today),
        "待提醒" => entries.Where(item => item.Status == EntryStatus.Active && item.DueAt is not null),
        "桌面便签" => entries.Where(item => item.Status != EntryStatus.Archived && item.DesktopCard.IsPinned),
        "收藏" => entries.Where(item => item.Status != EntryStatus.Archived && item.IsFavorite),
        "已完成" => entries.Where(item => item.Status == EntryStatus.Completed),
        "已归档" => entries.Where(item => item.Status == EntryStatus.Archived),
        _ => entries.Where(item => item.Status != EntryStatus.Archived),
    };

    private static EntryRow ToRow(EntryItem entry)
    {
        var normalized = entry.Content.ReplaceLineEndings("\n");
        var parts = normalized.Split('\n', 2);
        var title = FirstLine(entry.Content);
        var preview = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
            ? parts[1].Trim()
            : entry.Tags.Count > 0
                ? string.Join(" · ", entry.Tags.Select(tag => $"#{tag}"))
                : entry.DueAt is null ? "普通随记" : "提醒随记";
        if (preview.Length > 80)
        {
            preview = preview[..80] + "…";
        }
        var meta = entry.DueAt is { } dueAt
            ? FormatDue(dueAt)
            : entry.UpdatedAt.ToLocalTime().ToString("M月d日 HH:mm");
        var markers = string.Join(" · ", new[]
        {
            entry.IsFavorite ? "★" : string.Empty,
            entry.DesktopCard.IsPinned ? "桌面" : string.Empty,
            entry.Status == EntryStatus.Completed ? "完成" : string.Empty,
            entry.Status == EntryStatus.Archived ? "归档" : string.Empty,
        }.Where(value => value.Length > 0));
        return new EntryRow(entry.Id, title, preview, meta, markers, ColorBrush(entry.DesktopCard.Color));
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> operation)
    {
        if (_busy || _closingCancellation.IsCancellationRequested)
        {
            return;
        }
        _busy = true;
        try
        {
            await operation(_closingCancellation.Token);
        }
        catch (OperationCanceledException) when (_closingCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetGlobalStatus($"操作失败：{exception.Message}", isError: true);
            SetEditorStatus($"保存失败：{exception.Message}", isError: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private bool TryGetDueTime(out DateTimeOffset result, out string error)
    {
        result = default;
        error = string.Empty;
        if (DueDatePicker.SelectedDate is not { } date)
        {
            error = "请选择提醒日期。";
            return false;
        }
        if (!TimeSpan.TryParseExact(DueTimeBox.Text.Trim(), ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var time))
        {
            error = "时间格式应为 14:30。";
            return false;
        }
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
        {
            error = "这个本地时间不存在。";
            return false;
        }
        result = LocalDateTimeOffset(local);
        return true;
    }

    private static DateTimeOffset LocalDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(value));

    private void SetDueTime(DateTimeOffset target)
    {
        var local = target.ToLocalTime();
        DueDatePicker.SelectedDate = local.Date;
        DueTimeBox.Text = local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<string> ParseTags() => TagsBox.Text
        .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private RepeatRule CurrentRepeat() => RepeatBox.SelectedIndex switch
    {
        1 => RepeatRule.Daily,
        2 => RepeatRule.Weekdays,
        3 => RepeatRule.Weekly,
        _ => RepeatRule.None,
    };

    private void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void SetEditorStatus(string text, bool isError)
    {
        EditorStatusText.Foreground = isError ? Brushes.Firebrick : Brushes.SeaGreen;
        EditorStatusText.Text = text;
    }

    private void SetGlobalStatus(string text, bool isError)
    {
        GlobalStatusText.Foreground = isError ? Brushes.Firebrick : Brush("#756C7B");
        GlobalStatusText.Text = text;
    }

    private static string FirstLine(string content)
    {
        var value = content.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();
        if (value.Length == 0)
        {
            return "无标题随记";
        }
        return value.Length <= 30 ? value : value[..30] + "…";
    }

    private static string FormatDue(DateTimeOffset dueAt)
    {
        var local = dueAt.ToLocalTime();
        return local.Date == DateTime.Today
            ? $"今天 {local:HH:mm}"
            : local.Date == DateTime.Today.AddDays(1)
                ? $"明天 {local:HH:mm}"
                : local.ToString("M月d日 HH:mm");
    }

    private static string FormatRepeatSuffix(RepeatRule repeat) => repeat switch
    {
        RepeatRule.Daily => " · 每天",
        RepeatRule.Weekdays => " · 工作日",
        RepeatRule.Weekly => " · 每周",
        _ => string.Empty,
    };

    private static SolidColorBrush ColorBrush(string color) => Brush(color switch
    {
        "violet" => "#9A72C2",
        "mint" => "#72B787",
        "rose" => "#D58A99",
        "blue" => "#78A9D1",
        _ => "#D7BD57",
    });

    private static SolidColorBrush Brush(string value) =>
        new((Color)ColorConverter.ConvertFromString(value));

    private sealed record EntryRow(
        string Id,
        string Title,
        string Preview,
        string Meta,
        string Markers,
        SolidColorBrush ColorBrush);
}

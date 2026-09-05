using System.Diagnostics;
using System.IO;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DesktopOrganizer.Core.Models;
using DesktopOrganizer.Models;

namespace DesktopOrganizer.Views;

public partial class ZoneView : System.Windows.Controls.UserControl
{
    private const string ItemPathDataFormat = "DesktopOrganizer.ItemPath";
    private const string SourceZoneDataFormat = "DesktopOrganizer.SourceZone";
    private const double CollapsedHeight = ZoneLayout.CollapsedHeight;
    private const double CollapsedWidth = ZoneLayout.CollapsedWidth;

    private ZoneLayout _layout = new();
    private string _subtitle = "桌面项目";
    private ImageSource? _backdropSource;
    private System.Windows.Size _workspaceSize;
    private System.Windows.Point _dragStart;
    private DesktopEntry? _dragCandidate;
    private bool _isRenaming;
    private bool _isFocused;
    private string _smartRootPath = string.Empty;
    private string _smartCurrentPath = string.Empty;

    public ZoneView()
    {
        InitializeComponent();
    }

    public event EventHandler<ZoneLayoutChangedEventArgs>? LayoutChanged;

    public event EventHandler<ZoneItemDroppedEventArgs>? ItemDropped;

    public event EventHandler<ItemPathEventArgs>? ReturnToInboxRequested;

    public event EventHandler? DeleteRequested;

    public event EventHandler? Activated;

    public event EventHandler? PinToggleRequested;

    public event EventHandler? FocusToggleRequested;

    public event EventHandler? EditSmartZoneRequested;

    public event EventHandler<ItemPathEventArgs>? RenameFileRequested;

    public event EventHandler<FilePathsEventArgs>? RecycleFilesRequested;

    public event EventHandler<ItemPathEventArgs>? NavigateFolderRequested;

    public event EventHandler? NavigateRootRequested;

    public event EventHandler? NavigateUpRequested;

    public string ZoneId => _layout.Id;

    public string Subtitle
    {
        get => _subtitle;
        set
        {
            _subtitle = value;
            UpdateHeaderText();
        }
    }

    public void SetItems(IReadOnlyCollection<DesktopEntry> entries, int? totalCount = null)
    {
        ItemsList.ItemsSource = entries;
        EmptyState.Visibility = entries.Count == 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        EmptyStateText.Text = totalCount is not null
            ? "当前视域没有匹配结果"
            : _layout.Kind == ZoneKind.SmartFolder
                ? "此刻没有符合视域的文件"
                : _layout.IsInbox ? "新的桌面项目会在这里安静落下" : "把重要项目安放到这里";
        Subtitle = totalCount is not null && totalCount != entries.Count
            ? $"{entries.Count}/{totalCount} 个项目"
            : entries.Count == 1 ? "1 个项目" : $"{entries.Count} 个项目";
    }

    public void SetStatus(string message)
    {
        ItemsList.ItemsSource = null;
        EmptyState.Visibility = System.Windows.Visibility.Visible;
        EmptyStateText.Text = message;
        Subtitle = message;
    }

    public void SetBackdrop(ImageSource? source, System.Windows.Size workspaceSize)
    {
        _backdropSource = source;
        _workspaceSize = workspaceSize;
        GlassBackdropBrush.ImageSource = source;
        UpdateBackdropViewbox();
    }

    public void SetNavigationPath(string rootPath, string currentPath)
    {
        _smartRootPath = rootPath;
        _smartCurrentPath = currentPath;
        UpdateNavigationHeader();
    }

    public void ApplyFocusState(bool focusMode, bool isFocused)
    {
        _isFocused = focusMode && isFocused;
        IsHitTestVisible = !focusMode || isFocused;
        FocusButton.Background = _isFocused
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(52, 139, 124, 255))
            : System.Windows.Media.Brushes.Transparent;
        FocusGlyph.SetResourceReference(
            System.Windows.Shapes.Shape.StrokeProperty,
            _isFocused ? "AccentBrush" : "SecondaryTextBrush");
        FocusButton.ToolTip = _isFocused ? "退出视域" : "进入视域";
        System.Windows.Automation.AutomationProperties.SetName(
            FocusButton,
            _isFocused ? "退出当前视域" : "聚焦此分区");
        RestoreZoneBorder();

        var targetOpacity = focusMode && !isFocused ? 0.18 : 1.0;
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            });
    }

    public void ApplyLayout(ZoneLayout layout)
    {
        _layout = layout.Clone();
        Width = _layout.IsCollapsed ? CollapsedWidth : _layout.Width;
        Height = _layout.IsCollapsed ? CollapsedHeight : _layout.Height;
        RootSurface.Margin = _layout.IsCollapsed
            ? new System.Windows.Thickness(4)
            : new System.Windows.Thickness(10);
        System.Windows.Controls.Canvas.SetLeft(this, _layout.X);
        System.Windows.Controls.Canvas.SetTop(this, _layout.Y);
        ItemsList.Visibility = _layout.IsCollapsed ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        ResizeThumb.Visibility = _layout.IsCollapsed || _layout.IsPinned
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        HeaderRow.Height = new System.Windows.GridLength(_layout.IsCollapsed ? 36 : 52);
        ExpandedHeader.Visibility = _layout.IsCollapsed
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        CollapsedHeader.Visibility = _layout.IsCollapsed
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        BackdropSurface.Visibility = _layout.IsCollapsed
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        ZoneBorder.SetResourceReference(
            System.Windows.Controls.Border.BackgroundProperty,
            _layout.IsCollapsed ? "CollapsedGlassTintBrush" : "GlassTintBrush");
        ZoneShadowEffect.BlurRadius = _layout.IsCollapsed ? 14 : 34;
        ZoneShadowEffect.ShadowDepth = _layout.IsCollapsed ? 4 : 12;
        ZoneShadowEffect.Opacity = _layout.IsCollapsed ? 0.34 : 0.42;
        ChevronRotation.Angle = _layout.IsCollapsed ? 180 : 0;
        FocusColumn.Width = _layout.IsCollapsed
            ? new System.Windows.GridLength(0)
            : new System.Windows.GridLength(40);
        MoreColumn.Width = _layout.IsCollapsed
            ? new System.Windows.GridLength(0)
            : new System.Windows.GridLength(44);
        PinColumn.Width = _layout.IsCollapsed
            ? new System.Windows.GridLength(0)
            : new System.Windows.GridLength(40);
        FocusButton.Visibility = _layout.IsCollapsed
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        MoreButton.Visibility = _layout.IsCollapsed
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        PinRotation.Angle = _layout.IsPinned ? 0 : -28;
        PinButton.Background = _layout.IsPinned
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(52, 139, 124, 255))
            : System.Windows.Media.Brushes.Transparent;
        PinGlyph.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            _layout.IsPinned ? "AccentBrush" : "SecondaryTextBrush");
        PinButton.ToolTip = _layout.IsPinned ? "取消固定" : "固定分区";
        System.Windows.Automation.AutomationProperties.SetName(
            PinButton,
            _layout.IsPinned ? "取消固定分区" : "固定分区");
        MoveThumb.Cursor = _layout.IsPinned
            ? System.Windows.Input.Cursors.Arrow
            : System.Windows.Input.Cursors.SizeAll;
        CollapsedMoveThumb.Cursor = MoveThumb.Cursor;
        RenameMenuItem.IsEnabled = !_layout.IsPinned;
        EditSmartZoneMenuItem.IsEnabled = !_layout.IsPinned;
        DeleteMenuItem.IsEnabled = !_layout.IsPinned;
        var isSmartFolder = _layout.Kind == ZoneKind.SmartFolder;
        SmartMetaBar.Visibility = isSmartFolder
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        EditSmartZoneMenuItem.Visibility = isSmartFolder
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        OpenSourceFolderMenuItem.Visibility = EditSmartZoneMenuItem.Visibility;
        SmartZoneMenuSeparator.Visibility = EditSmartZoneMenuItem.Visibility;
        ReturnToInboxMenuItem.Visibility = !isSmartFolder && !_layout.IsInbox
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        ReturnToInboxSeparator.Visibility = ReturnToInboxMenuItem.Visibility;
        var effectiveDisplayMode = _layout.DisplayMode == ZoneDisplayMode.Auto
            ? isSmartFolder ? ZoneDisplayMode.List : ZoneDisplayMode.Grid
            : _layout.DisplayMode;
        var useList = effectiveDisplayMode == ZoneDisplayMode.List;
        ItemsList.ItemContainerStyle = (System.Windows.Style)FindResource(
            useList ? "SmartItemStyle" : "DesktopItemStyle");
        ItemsList.ItemTemplate = (System.Windows.DataTemplate)FindResource(
            useList ? "SmartItemTemplate" : "DesktopItemTemplate");
        ItemsList.ItemsPanel = (System.Windows.Controls.ItemsPanelTemplate)FindResource(
            useList ? "SmartItemsPanel" : "DesktopItemsPanel");
        ItemsList.Margin = isSmartFolder
            ? new System.Windows.Thickness(10, 46, 6, 10)
            : new System.Windows.Thickness(10, 8, 6, 10);
        EmptyState.Margin = isSmartFolder
            ? new System.Windows.Thickness(24, 46, 24, 24)
            : new System.Windows.Thickness(24);
        if (isSmartFolder && string.IsNullOrWhiteSpace(_smartRootPath))
        {
            _smartRootPath = _layout.SourcePath;
            _smartCurrentPath = _layout.SourcePath;
        }

        UpdateNavigationHeader();
        SmartFilterText.Text = _layout.Extensions.Count == 0
            ? "全部格式"
            : string.Join(" · ", _layout.Extensions.Take(3).Select(extension => extension.TrimStart('.').ToUpperInvariant()));
        AutoDisplayModeMenuItem.IsChecked = _layout.DisplayMode == ZoneDisplayMode.Auto;
        GridDisplayModeMenuItem.IsChecked = _layout.DisplayMode == ZoneDisplayMode.Grid;
        ListDisplayModeMenuItem.IsChecked = _layout.DisplayMode == ZoneDisplayMode.List;
        SmartDisplayModeGlyph.Text = useList ? "▦" : "☷";
        SmartDisplayModeButton.ToolTip = useList ? "切换到图标视图" : "切换到列表视图";
        System.Windows.Automation.AutomationProperties.SetName(
            SmartDisplayModeButton,
            useList ? "切换到图标视图" : "切换到列表视图");
        UpdateBackdropViewbox();
        UpdateHeaderText();
    }

    public void BeginRename()
    {
        if (_isRenaming || _layout.IsPinned)
        {
            return;
        }

        _isRenaming = true;
        RenameTextBox.Text = _layout.Name;
        MoveThumb.Visibility = System.Windows.Visibility.Collapsed;
        RenameTextBox.Visibility = System.Windows.Visibility.Visible;
        RenameTextBox.Focus();
        RenameTextBox.SelectAll();
    }

    private void OnMoveDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_layout.IsPinned || Parent is not System.Windows.FrameworkElement parent)
        {
            return;
        }

        _layout.X += e.HorizontalChange;
        _layout.Y += e.VerticalChange;
        _layout.ClampTo(parent.ActualWidth, parent.ActualHeight);
        ApplyLayout(_layout);
        RaiseLayoutChanged();
    }

    private void OnResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_layout.IsCollapsed || _layout.IsPinned || Parent is not System.Windows.FrameworkElement parent)
        {
            return;
        }

        _layout.Width += e.HorizontalChange;
        _layout.Height += e.VerticalChange;
        _layout.ClampTo(parent.ActualWidth, parent.ActualHeight);
        ApplyLayout(_layout);
        RaiseLayoutChanged();
    }

    private void OnCollapseClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var previousWidth = ActualWidth > 0 ? ActualWidth : Width;
        var previousHeight = ActualHeight > 0 ? ActualHeight : Height;
        _layout.IsCollapsed = !_layout.IsCollapsed;
        if (Parent is System.Windows.FrameworkElement parent)
        {
            _layout.ClampTo(parent.ActualWidth, parent.ActualHeight);
        }

        ApplyLayout(_layout);
        AnimateSizeTransition(previousWidth, previousHeight, Width, Height);
        RaiseLayoutChanged();
    }

    private void OnCollapsedHeaderDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OnCollapseClick(sender, e);
        e.Handled = true;
    }

    private void AnimateSizeTransition(
        double previousWidth,
        double previousHeight,
        double targetWidth,
        double targetHeight)
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(
            WidthProperty,
            new DoubleAnimation(previousWidth, targetWidth, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        BeginAnimation(
            HeightProperty,
            new DoubleAnimation(previousHeight, targetHeight, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
    }

    private void OnPinClick(object sender, System.Windows.RoutedEventArgs e) =>
        PinToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OnFocusClick(object sender, System.Windows.RoutedEventArgs e) =>
        FocusToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OnHeaderDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        FocusToggleRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnMoreClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is null)
        {
            return;
        }

        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.IsOpen = true;
    }

    private void OnRenameMenuClick(object sender, System.Windows.RoutedEventArgs e) => BeginRename();

    private void OnAutoDisplayModeClick(object sender, System.Windows.RoutedEventArgs e) =>
        SetDisplayMode(ZoneDisplayMode.Auto);

    private void OnGridDisplayModeClick(object sender, System.Windows.RoutedEventArgs e) =>
        SetDisplayMode(ZoneDisplayMode.Grid);

    private void OnListDisplayModeClick(object sender, System.Windows.RoutedEventArgs e) =>
        SetDisplayMode(ZoneDisplayMode.List);

    private void OnDisplayModeToggleClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var currentMode = _layout.DisplayMode == ZoneDisplayMode.Auto
            ? _layout.Kind == ZoneKind.SmartFolder ? ZoneDisplayMode.List : ZoneDisplayMode.Grid
            : _layout.DisplayMode;
        SetDisplayMode(currentMode == ZoneDisplayMode.List
            ? ZoneDisplayMode.Grid
            : ZoneDisplayMode.List);
    }

    private void OnNavigateRootClick(object sender, System.Windows.RoutedEventArgs e) =>
        NavigateRootRequested?.Invoke(this, EventArgs.Empty);

    private void OnNavigateUpClick(object sender, System.Windows.RoutedEventArgs e) =>
        NavigateUpRequested?.Invoke(this, EventArgs.Empty);

    private void SetDisplayMode(ZoneDisplayMode displayMode)
    {
        if (_layout.DisplayMode == displayMode)
        {
            return;
        }

        _layout.DisplayMode = displayMode;
        ApplyLayout(_layout);
        RaiseLayoutChanged();
    }

    private void OnEditSmartZoneMenuClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_layout.Kind == ZoneKind.SmartFolder && !_layout.IsPinned)
        {
            EditSmartZoneRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnOpenSourceFolderMenuClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_layout.Kind == ZoneKind.SmartFolder && Directory.Exists(_layout.SourcePath))
        {
            Process.Start(new ProcessStartInfo(_layout.SourcePath) { UseShellExecute = true });
        }
    }

    private void OnDeleteMenuClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_layout.IsPinned)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRenameKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void OnRenameLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (_isRenaming)
        {
            CommitRename();
        }
    }

    private void CommitRename()
    {
        var name = RenameTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            _layout.Name = name;
        }

        EndRename();
        UpdateHeaderText();
        RaiseLayoutChanged();
    }

    private void CancelRename()
    {
        EndRename();
        UpdateHeaderText();
    }

    private void EndRename()
    {
        _isRenaming = false;
        RenameTextBox.Visibility = System.Windows.Visibility.Collapsed;
        MoveThumb.Visibility = System.Windows.Visibility.Visible;
    }

    private void OnPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        var source = e.OriginalSource as System.Windows.DependencyObject;
        var item = source is null ? null : ItemsList.ContainerFromElement(source) as System.Windows.Controls.ListBoxItem;
        _dragCandidate = item?.DataContext as DesktopEntry;
    }

    private void OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _dragCandidate is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < System.Windows.SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < System.Windows.SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var entry = _dragCandidate;
        _dragCandidate = null;
        var selectedPaths = ItemsList.SelectedItems
            .OfType<DesktopEntry>()
            .Select(selected => selected.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!selectedPaths.Contains(entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            selectedPaths = [entry.Path];
        }

        var data = new System.Windows.DataObject();
        data.SetData(ItemPathDataFormat, selectedPaths);
        data.SetData(SourceZoneDataFormat, _layout.Id);
        System.Windows.DragDrop.DoDragDrop(ItemsList, data, System.Windows.DragDropEffects.Move);
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(ItemPathDataFormat)
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ItemPathDataFormat))
        {
            ZoneBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 124, 255));
        }
    }

    private void OnDragLeave(object sender, System.Windows.DragEventArgs e) => RestoreZoneBorder();

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(ItemPathDataFormat) is string[] paths &&
            e.Data.GetData(SourceZoneDataFormat) is string sourceZoneId)
        {
            ItemDropped?.Invoke(this, new ZoneItemDroppedEventArgs(_layout.Id, sourceZoneId, paths));
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
        }

        RestoreZoneBorder();
    }

    private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenSelectedItem();
    }

    private void OnItemsListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OpenSelectedItem();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.F2)
        {
            RequestRenameSelectedFile();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Delete)
        {
            RequestRecycleSelectedFiles();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.A &&
                 System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            ItemsList.SelectAll();
            e.Handled = true;
        }
    }

    private void OnItemsListPreviewMouseRightButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not System.Windows.DependencyObject source)
        {
            return;
        }

        if (ItemsList.ContainerFromElement(source) is System.Windows.Controls.ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void OnOpenItemClick(object sender, System.Windows.RoutedEventArgs e) => OpenSelectedItem();

    private void OnRenameFileClick(object sender, System.Windows.RoutedEventArgs e) => RequestRenameSelectedFile();

    private void OnRecycleFilesClick(object sender, System.Windows.RoutedEventArgs e) => RequestRecycleSelectedFiles();

    private void OnRevealItemClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not DesktopEntry entry)
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add($"/select,{entry.Path}");
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"无法在资源管理器中显示“{entry.Name}”。\n\n{exception.Message}",
                "Grid",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OnReturnToInboxItemClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_layout.IsInbox && ItemsList.SelectedItem is DesktopEntry entry)
        {
            ReturnToInboxRequested?.Invoke(this, new ItemPathEventArgs(entry.Path));
        }
    }

    private void RequestRenameSelectedFile()
    {
        if (ItemsList.SelectedItems.Count == 1 && ItemsList.SelectedItem is DesktopEntry entry)
        {
            RenameFileRequested?.Invoke(this, new ItemPathEventArgs(entry.Path));
        }
    }

    private void RequestRecycleSelectedFiles()
    {
        var paths = ItemsList.SelectedItems
            .OfType<DesktopEntry>()
            .Select(entry => entry.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length > 0)
        {
            RecycleFilesRequested?.Invoke(this, new FilePathsEventArgs(paths));
        }
    }

    private void OpenSelectedItem()
    {
        if (ItemsList.SelectedItem is not DesktopEntry entry)
        {
            return;
        }

        if (_layout.Kind == ZoneKind.SmartFolder && entry.IsDirectory)
        {
            NavigateFolderRequested?.Invoke(this, new ItemPathEventArgs(entry.Path));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"无法打开“{entry.Name}”。\n\n{exception.Message}",
                "Grid",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void RaiseLayoutChanged()
    {
        LayoutChanged?.Invoke(this, new ZoneLayoutChangedEventArgs(_layout.Clone()));
    }

    private void UpdateHeaderText()
    {
        // Keep header state on the templated control itself. Updating template children
        // directly is unreliable while a zone is being recreated (for example after undo).
        MoveThumb.Tag = _layout.Name;
        CollapsedMoveThumb.Tag = _layout.Name;
        System.Windows.Automation.AutomationProperties.SetHelpText(MoveThumb, _subtitle);
        System.Windows.Automation.AutomationProperties.SetHelpText(CollapsedMoveThumb, _subtitle);
        System.Windows.Automation.AutomationProperties.SetName(MoveThumb, $"{_layout.Name}，{_subtitle}");
        System.Windows.Automation.AutomationProperties.SetName(CollapsedMoveThumb, $"{_layout.Name}，{_subtitle}，双击展开");
    }

    private void UpdateNavigationHeader()
    {
        if (_layout.Kind != ZoneKind.SmartFolder)
        {
            return;
        }

        var rootPath = string.IsNullOrWhiteSpace(_smartRootPath) ? _layout.SourcePath : _smartRootPath;
        var currentPath = string.IsNullOrWhiteSpace(_smartCurrentPath) ? rootPath : _smartCurrentPath;
        var trimmedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootName = Path.GetFileName(trimmedRoot);
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = Path.GetPathRoot(rootPath) ?? rootPath;
        }

        string relative;
        try
        {
            relative = Path.GetRelativePath(rootPath, currentPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            relative = ".";
            currentPath = rootPath;
        }

        SmartPathText.Text = relative == "."
            ? $"▣  {rootName}"
            : $"▣  {rootName} › {relative.Replace("\\", " › ").Replace("/", " › ")}";
        SmartPathText.ToolTip = currentPath;
        var atRoot = string.Equals(
            Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(currentPath).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
        SmartRootButton.IsEnabled = !atRoot;
        SmartUpButton.IsEnabled = !atRoot;
    }

    private void UpdateBackdropViewbox()
    {
        if (_backdropSource is not BitmapSource bitmap ||
            _workspaceSize.Width <= 0 ||
            _workspaceSize.Height <= 0 ||
            bitmap.PixelWidth <= 0 ||
            bitmap.PixelHeight <= 0)
        {
            return;
        }

        var scale = Math.Max(
            _workspaceSize.Width / bitmap.PixelWidth,
            _workspaceSize.Height / bitmap.PixelHeight);
        var renderedWidth = bitmap.PixelWidth * scale;
        var renderedHeight = bitmap.PixelHeight * scale;
        var cropX = (renderedWidth - _workspaceSize.Width) / 2;
        var cropY = (renderedHeight - _workspaceSize.Height) / 2;

        const double inset = 10;
        var visibleHeight = _layout.IsCollapsed ? CollapsedHeight : _layout.Height;
        var sourceX = (cropX + _layout.X + inset) / scale;
        var sourceY = (cropY + _layout.Y + inset) / scale;
        var sourceWidth = Math.Max(1, (_layout.Width - (inset * 2)) / scale);
        var sourceHeight = Math.Max(1, (visibleHeight - (inset * 2)) / scale);

        sourceX = Math.Clamp(sourceX, 0, Math.Max(0, bitmap.PixelWidth - sourceWidth));
        sourceY = Math.Clamp(sourceY, 0, Math.Max(0, bitmap.PixelHeight - sourceHeight));
        sourceWidth = Math.Min(sourceWidth, bitmap.PixelWidth - sourceX);
        sourceHeight = Math.Min(sourceHeight, bitmap.PixelHeight - sourceY);

        GlassBackdropBrush.Viewbox = new System.Windows.Rect(sourceX, sourceY, sourceWidth, sourceHeight);
    }

    private void RestoreZoneBorder()
    {
        if (_isFocused)
        {
            ZoneBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 139, 124, 255));
        }
        else
        {
            ZoneBorder.SetResourceReference(
                System.Windows.Controls.Border.BorderBrushProperty,
                "ZoneBorderBrush");
        }
    }
}

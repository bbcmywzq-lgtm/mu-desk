using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopOrganizer.Services;
using PersonalToolbox.Modules;
using PersonalToolbox.Native;
using Toolbox.Core;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfImage = System.Windows.Controls.Image;

namespace PersonalToolbox.Views;

public partial class FileShelfWindow : Window
{
    private readonly FileShelfModule _module;
    private readonly FileShelfSettings _settings;
    private System.Windows.Point _dragStart;
    private string[]? _dragItemIds;
    private bool _closingForModuleStop;

    public FileShelfWindow(FileShelfModule module, FileShelfSettings settings)
    {
        InitializeComponent();
        _module = module;
        _settings = settings;
        _module.DocumentChanged += Module_OnDocumentChanged;
        InitializeMotion();
        RenderDocument();
    }

    public bool IsExpanded { get; private set; }

    public void ShowCollapsed()
    {
        _collapseTimer.Stop();
        if (IsKeyboardFocusWithin) Keyboard.ClearFocus();
        Transition(false);
    }

    public void ShowExpanded(bool activate)
    {
        _collapseTimer.Stop();
        Transition(true);
        if (activate)
        {
            Activate();
            Focus();
        }
        ScheduleCollapse(4000);
    }

    public void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusPanel.Background = Brush(isError ? "#573139" : "#35334A");
        StatusPanel.BorderBrush = Brush(isError ? "#D78791" : "#8E79C6");
        StatusPanel.Visibility = Visibility.Visible;
    }

    public void CloseForModuleStop()
    {
        _closingForModuleStop = true;
        _module.DocumentChanged -= Module_OnDocumentChanged;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_closingForModuleStop)
        {
            e.Cancel = true;
            ShowCollapsed();
        }

        base.OnClosing(e);
    }

    private void Window_OnSourceInitialized(object? sender, EventArgs e)
    {
        FileShelfWindowInterop.MarkAsToolWindow(this);
        ApplyHostBounds(_hostBounds);
    }

    private void Module_OnDocumentChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RenderDocument);

    private void RenderDocument()
    {
        var document = _module.Snapshot();
        CollapsedCountText.Text = document.ItemCount > 99 ? "99+" : document.ItemCount.ToString();
        CollapsedButton.ToolTip = $"Drop · {document.ItemCount} 项\n点击展开，也可直接拖入文件";
        SummaryText.Text = document.PinnedCount > 0
            ? $"{document.ItemCount} 项 · {document.PinnedCount} 项已固定"
            : $"{document.ItemCount} 项";
        UndoButton.Visibility = _module.CanUndo ? Visibility.Visible : Visibility.Hidden;
        ClearButton.IsEnabled = document.ItemCount > document.PinnedCount;
        BatchesPanel.Children.Clear();

        if (document.ItemCount == 0)
        {
            BatchesPanel.Children.Add(new TextBlock
            {
                Margin = new Thickness(12, 64, 12, 0),
                Foreground = Brush("#AAA2B6"),
                FontSize = 13,
                LineHeight = 26,
                Text = "文件，先放这里\n拖入本地文件或文件夹\n切换窗口后，再从这里拖走",
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var batch in document.Batches)
        {
            BatchesPanel.Children.Add(CreateBatch(batch));
        }
    }

    private FrameworkElement CreateBatch(FileShelfBatch batch)
    {
        var body = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Foreground = Brush("#D8D5DE"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Text = $"{FormatTime(batch.AddedAt)} · {batch.Items.Count} 项",
        });
        var dragGroup = new Border
        {
            Tag = batch.Items.Select(item => item.Id).ToArray(),
            Padding = new Thickness(7, 3, 7, 3),
            CornerRadius = new CornerRadius(7),
            BorderBrush = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Background = Brush("#18FFFFFF"),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new TextBlock { Foreground = WpfBrushes.White, FontSize = 10, Text = "拖出整组" },
        };
        dragGroup.PreviewMouseLeftButtonDown += DragSource_OnMouseLeftButtonDown;
        dragGroup.PreviewMouseMove += DragSource_OnMouseMove;
        Grid.SetColumn(dragGroup, 1);
        header.Children.Add(dragGroup);
        body.Children.Add(header);

        foreach (var item in batch.Items)
        {
            body.Children.Add(CreateItemRow(item));
        }

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(0),
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Child = body,
        };
    }

    private FrameworkElement CreateItemRow(FileShelfItem item)
    {
        var available = item.Kind == FileShelfItemKind.Directory ? Directory.Exists(item.Path) : File.Exists(item.Path);
        var grid = new Grid { Opacity = available ? 1 : 0.62 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = ShellIconProvider.GetSmallIcon(item.Path);
        if (icon is not null)
        {
            grid.Children.Add(new WpfImage { Width = 20, Height = 20, Source = icon, VerticalAlignment = VerticalAlignment.Center });
        }

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Foreground = WpfBrushes.White,
            Text = item.DisplayName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = item.Path,
        });
        copy.Children.Add(new TextBlock
        {
            Foreground = available ? Brush("#AAA7B1") : Brush("#E0A7AF"),
            FontSize = 10,
            Text = available ? (item.Kind == FileShelfItemKind.Directory ? "文件夹" : "文件") : "不可用",
        });
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);

        var pin = new WpfButton
        {
            Width = 42,
            Height = 28,
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(0),
            FontSize = 11,
            Content = item.IsPinned ? "已固定" : "固定",
            Style = (Style)FindResource("ShelfButton"),
            ToolTip = item.IsPinned ? "取消固定；以后拖出时将从货架移除" : "固定后，拖出文件时仍保留在货架",
            Tag = item,
            Foreground = WpfBrushes.White,
            Background = item.IsPinned ? Brush("#487A4FD3") : WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
        };
        pin.Click += PinButton_OnClick;
        Grid.SetColumn(pin, 2);
        grid.Children.Add(pin);

        var remove = new WpfButton
        {
            Width = 30,
            Height = 28,
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(0),
            FontSize = 11,
            Content = "×",
            Style = (Style)FindResource("ShelfButton"),
            ToolTip = "只从 Drop 移除，不删除原文件",
            Tag = item.Id,
            Foreground = Brush("#B9B0C6"),
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
        };
        remove.Click += RemoveButton_OnClick;
        System.Windows.Automation.AutomationProperties.SetName(remove, $"移除 {item.DisplayName}");
        System.Windows.Automation.AutomationProperties.SetName(pin, $"{(item.IsPinned ? "取消固定" : "固定")} {item.DisplayName}");
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);

        var row = new Border
        {
            Tag = new[] { item.Id },
            MinHeight = 56,
            Margin = new Thickness(0, 0, 0, 5),
            Padding = new Thickness(7, 4, 5, 4),
            Background = Brush("#0EFFFFFF"),
            BorderBrush = available ? Brush("#14FFFFFF") : Brush("#85545C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = grid,
            Cursor = available ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
        };
        if (available)
        {
            row.PreviewMouseLeftButtonDown += DragSource_OnMouseLeftButtonDown;
            row.PreviewMouseMove += DragSource_OnMouseMove;
        }

        return row;
    }

    private void DragSource_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<WpfButton>(e.OriginalSource as DependencyObject) is not null)
        {
            _dragItemIds = null;
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragItemIds = (sender as FrameworkElement)?.Tag as string[];
    }

    private void DragSource_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItemIds is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var ids = _dragItemIds;
        _dragItemIds = null;
        var snapshot = _module.Snapshot();
        var items = snapshot.Batches.SelectMany(batch => batch.Items)
            .Where(item => ids.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
            .Where(item => item.Kind == FileShelfItemKind.Directory ? Directory.Exists(item.Path) : File.Exists(item.Path))
            .ToArray();
        if (items.Length == 0)
        {
            ShowStatus("这些路径当前不可用。", isError: true);
            RenderDocument();
            return;
        }

        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, items.Select(item => item.Path).ToArray());
        System.Windows.DragDropEffects effect;
        _outgoingDrag = true;
        _collapseTimer.Stop();
        try
        {
            effect = System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, data,
                System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Link);
        }
        finally { _outgoingDrag = false; _incomingDrag = false; ScheduleCollapse(); }
        if (effect == System.Windows.DragDropEffects.None)
        {
            ShowStatus("没有移出，项目仍在货架上。", isError: false);
            return;
        }

        var unpinned = items.Where(item => !item.IsPinned).Select(item => item.Id).ToArray();
        if (unpinned.Length == 0)
        {
            ShowStatus("已拖出；固定项目仍保留。", isError: false);
        }
        else if (_module.TryRemove(unpinned, includePinned: false, out var message))
        {
            ShowStatus("已拖出；未固定项目已从货架移除。", isError: false);
        }
        else
        {
            ShowStatus(message, isError: true);
        }
    }

    private void PinButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as WpfButton)?.Tag is not FileShelfItem item)
        {
            return;
        }

        if (!_module.TrySetPinned(item.Id, !item.IsPinned, out var message))
        {
            ShowStatus(message, isError: true);
        }
    }

    private void RemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as WpfButton)?.Tag is not string itemId)
        {
            return;
        }

        var success = _module.TryRemove([itemId], includePinned: true, out var message);
        ShowStatus(message, isError: !success);
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        _confirming = true;
        _collapseTimer.Stop();
        MessageBoxResult answer;
        try { answer = System.Windows.MessageBox.Show(this,
            "只会清空货架里的未固定路径，不会删除、移动或修改原文件。",
            "清空未固定项？",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information,
            MessageBoxResult.Cancel); }
        finally { _confirming = false; ScheduleCollapse(); }
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        var success = _module.TryClearUnpinned(out var message);
        ShowStatus(message, isError: !success);
    }

    private void UndoButton_OnClick(object sender, RoutedEventArgs e)
    {
        var success = _module.TryUndo(out var message);
        ShowStatus(message, isError: !success);
    }

    private void Window_OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (!IsExpanded && !_outgoingDrag && TryGetFileDrop(e.Data, out _))
        {
            ShowExpanded(activate: false);
        }

        UpdateDragFeedback(e);
    }

    private void Window_OnDragOver(object sender, System.Windows.DragEventArgs e) => Window_OnDragEnter(sender, e);

    private void Window_OnDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        _incomingDrag = false;
        DropHint.Background = Brush("#14FFFFFF");
        DropHint.BorderBrush = Brush("#25FFFFFF");
        DropHintText.Text = "拖入暂存，拖出继续使用";
        ScheduleCollapse();
    }

    private void Window_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = System.Windows.DragDropEffects.None;
        _incomingDrag = false;
        // Dragging back onto this same shelf is cancellation, not a successful
        // external copy (which would otherwise remove the original metadata).
        if (_outgoingDrag) { Window_OnDragLeave(sender, e); return; }
        if (!TryGetFileDrop(e.Data, out var paths))
        {
            ShowStatus("这里只接收已经落盘的本地文件或文件夹。", isError: true);
            ScheduleCollapse();
            return;
        }

        var success = _module.TryAddPaths(paths, out var message);
        e.Effects = success ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        ShowStatus(message, isError: !success);
        Window_OnDragLeave(sender, e);
    }

    private void UpdateDragFeedback(System.Windows.DragEventArgs e)
    {
        _incomingDrag = true;
        _collapseTimer.Stop();
        var valid = TryGetFileDrop(e.Data, out var paths) && !_outgoingDrag;
        e.Effects = valid ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
        DropHint.Background = Brush(valid ? "#4B376E" : "#49343A");
        DropHint.BorderBrush = Brush(valid ? "#A98CE2" : "#D78791");
        DropHintText.Text = _outgoingDrag ? "拖到其他窗口，或松开取消" : valid ? $"松手暂存 · {paths.Length} 项" : "这里只接收本地文件或文件夹";
    }

    private static bool TryGetFileDrop(System.Windows.IDataObject data, out string[] paths)
    {
        paths = [];
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
            data.GetData(System.Windows.DataFormats.FileDrop) is not string[] dropped)
        {
            return false;
        }

        paths = dropped
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return paths.Length > 0;
    }

    private void CollapsedButton_OnClick(object sender, RoutedEventArgs e) => ShowExpanded(activate: true);

    private void CollapseButton_OnClick(object sender, RoutedEventArgs e) => ShowCollapsed();

    private void Window_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ShowCollapsed();
            e.Handled = true;
        }
    }


    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.Date == DateTimeOffset.Now.Date ? value.ToString("HH:mm") : value.ToString("M月d日 HH:mm");

    private static SolidColorBrush Brush(string value) =>
        new((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value));
}

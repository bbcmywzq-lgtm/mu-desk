using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace PersonalToolbox.Views;

public partial class CueToolbarWindow
{
    private ContextMenu? _openMenu;
    private void RefreshAnnotation()
    {
        var a = _module.Annotation;
        if (a is null) return;
        PenTool.IsChecked = a.Tool == "Pen"; HighlightTool.IsChecked = a.Tool == "Highlighter";
        ShapeTool.IsChecked = a.Tool is "Arrow" or "Line" or "Rectangle" or "Ellipse";
        ShapeTool.Content = ShapeName(a.LastShape);
        TextTool.IsChecked = a.Tool == "Text"; StepTool.IsChecked = a.Tool == "Step"; EraseTool.IsChecked = a.Tool == "Erase";
        StepTool.ToolTip = $"下一个编号：{a.Document.NextNumber}";
        ColorTool.Foreground = new SolidColorBrush(a.CurrentColor);
        SizeTool.Content = $"{a.CurrentSize:0} px";
        SizeTool.ToolTip = a.Tool == "Text" ? "字号" : "线宽";
        SizeTool.Visibility = a.Tool is "Erase" or "Step" ? Visibility.Collapsed : Visibility.Visible;
        ColorTool.Visibility = a.Tool == "Erase" ? Visibility.Collapsed : Visibility.Visible;
        UndoTool.IsEnabled = a.Document.CanUndo; RedoTool.IsEnabled = a.Document.CanRedo;
        FreezeTool.IsChecked = a.IsFrozen; FreezeTool.Content = a.IsFrozen ? "已冻结" : "实时";
        FreezeTool.ToolTip = a.IsFrozen ? "背景已冻结；点击回到实时画面" : "实时画面；点击冻结背景";
        DesktopTool.IsChecked = a.IsOperatingDesktop; DesktopTool.Content = a.IsOperatingDesktop ? "绘图" : "桌面";
        DesktopTool.ToolTip = a.IsOperatingDesktop ? "正在操作桌面；点击恢复绘图" : "临时操作底下的软件（会解除冻结）";
        AnnotationTools.IsEnabled = !a.IsBusy;
    }
    private static string ShapeName(string tool) => tool switch { "Line" => "直线", "Rectangle" => "矩形", "Ellipse" => "椭圆", _ => "箭头" };
    private void AnnotationTool_OnClick(object sender, RoutedEventArgs e)
        => _module.Annotation?.SelectTool((string)((ButtonBase)sender).CommandParameter);
    private void Undo_OnClick(object sender, RoutedEventArgs e) => _module.Annotation?.Undo();
    private void Redo_OnClick(object sender, RoutedEventArgs e) => _module.Annotation?.Redo();
    private void Desktop_OnClick(object sender, RoutedEventArgs e) => _module.Annotation?.ToggleDesktop();
    private void ExitAnnotation_OnClick(object sender, RoutedEventArgs e) => _module.ExitAnnotation();
    private async void Freeze_OnClick(object sender, RoutedEventArgs e) => await AnnotationAction(async () => { if (_module.Annotation is { } a) await a.ToggleFreezeAsync(); });
    private async Task AnnotationAction(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "标注操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Refresh(); }
    }
    private ContextMenu Menu(FrameworkElement anchor)
    {
        if (_openMenu is not null) _openMenu.IsOpen = false;
        _collapseTimer.Stop();
        var menu = new ContextMenu { Style = (Style)FindResource("CueMenu"), PlacementTarget = anchor,
            Placement = _bottomDock ? PlacementMode.Top : PlacementMode.Bottom, VerticalOffset = _bottomDock ? -10 : 10 };
        _openMenu = menu;
        menu.Closed += (_, _) => Refresh();
        return menu;
    }
    private void Add(ContextMenu menu, object label, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = label, Style = (Style)FindResource("CueMenuItem"), IsEnabled = enabled };
        item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    private void ShapeMenu_OnClick(object sender, RoutedEventArgs e)
    {
        var a = _module.Annotation; if (a is null) return;
        var menu = Menu((FrameworkElement)sender);
        foreach (var tool in new[] { "Arrow", "Line", "Rectangle", "Ellipse" })
            Add(menu, (a.LastShape == tool ? "✓  " : "    ") + ShapeName(tool), () => a.SelectTool(tool));
        menu.IsOpen = true; Refresh();
    }
    private void ColorMenu_OnClick(object sender, RoutedEventArgs e)
    {
        var a = _module.Annotation; if (a is null) return;
        var menu = Menu((FrameworkElement)sender);
        foreach (var (name, hex) in new[] { ("紫色", "#9D71EE"), ("珊瑚红", "#FF5268"), ("金黄", "#FFD358"), ("青蓝", "#39C5D8"), ("白色", "#FFFFFF"), ("墨黑", "#24232C") })
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var label = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            label.Children.Add(new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(color), Margin = new Thickness(0, 0, 10, 0) });
            label.Children.Add(new System.Windows.Controls.TextBlock { Text = name + (a.CurrentColor == color ? "  ✓" : "") });
            Add(menu, label, () => a.SetColor(color));
        }
        menu.IsOpen = true;
    }
    private void SizeMenu_OnClick(object sender, RoutedEventArgs e)
    {
        var a = _module.Annotation; if (a is null) return;
        var menu = Menu((FrameworkElement)sender);
        var sizes = a.Tool == "Text" ? new[] { 16d, 22, 30, 42 } : a.Tool == "Highlighter" ? new[] { 12d, 20, 30 } : new[] { 2d, 4, 7 };
        foreach (var size in sizes) Add(menu, $"{(a.CurrentSize == size ? "✓" : "  ")}  {size:0} px", () => a.SetSize(size));
        menu.IsOpen = true;
    }
    private void MoreMenu_OnClick(object sender, RoutedEventArgs e)
    {
        var a = _module.Annotation; if (a is null) return;
        var menu = Menu((FrameworkElement)sender);
        Add(menu, "复制标注图片", async () => await AnnotationAction(a.CopyAsync));
        Add(menu, "保存为 PNG…", async () => await AnnotationAction(a.SaveAsync));
        Add(menu, "清空标注（可撤销）", a.ClearMarks, a.Document.Items.Count > 0);
        menu.IsOpen = true;
    }
}

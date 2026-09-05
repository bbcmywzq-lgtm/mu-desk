using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PersonalToolbox.Modules;
using PersonalToolbox.Native;

namespace PersonalToolbox.Views;

public partial class CursorGalleryWindow : Window
{
    private readonly CursorLibraryService _libraryService;
    private IReadOnlyList<CursorSkinPackage> _packages = [];
    private bool _canApply;

    public CursorGalleryWindow(CursorLibraryService libraryService)
    {
        InitializeComponent();
        TaskbarWindowIdentity.Apply(this, TaskbarWindowIdentity.CursorGallery);
        _libraryService = libraryService;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        Reload();
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (SkinsList.SelectedItem is not CursorSkinPackage package)
        {
            return;
        }

        try
        {
            _libraryService.Apply(package);
            Reload(package.Id);
            ShowFeedback($"已应用“{package.Name}”。", isError: false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            ShowFeedback(exception.Message, isError: true);
        }
    }

    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _libraryService.ResetToWindowsDefault();
            Reload();
            ShowFeedback("已经恢复 Windows 默认光标。", isError: false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            ShowFeedback(exception.Message, isError: true);
        }
    }

    private void SkinsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelection();
    }

    private void Reload(string? preferredId = null)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        var result = _libraryService.Load();
        _packages = result.Packages;
        _canApply = result.TargetEstablished;
        SkinsList.ItemsSource = _packages;
        CollectionCountText.Text = $"{_packages.Count} 套皮肤";
        EmptyState.Visibility = _packages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ErrorBanner.Visibility = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ErrorText.Text = result.ErrorMessage ?? string.Empty;

        var selected = preferredId is null
            ? _packages.FirstOrDefault(package => package.IsApplied) ?? _packages.FirstOrDefault()
            : _packages.FirstOrDefault(package => string.Equals(package.Id, preferredId, StringComparison.Ordinal))
                ?? _packages.FirstOrDefault();
        SkinsList.SelectedItem = selected;
        if (selected is not null)
        {
            SkinsList.ScrollIntoView(selected);
        }

        var current = _packages.FirstOrDefault(package => package.IsApplied);
        CurrentSchemeText.Text = current is null ? "当前：Windows / 其他方案" : $"当前：{current.Name}";
        if (result.Migrated)
        {
            ShowFeedback("已复制旧光标库；旧文件仍保留在原位置。", isError: false);
        }
        else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            ShowFeedback("光标库需要处理，详情见上方提示。", isError: true);
        }
        else if (_packages.Count == 0)
        {
            ShowFeedback("没有找到可读取的光标皮肤库。", isError: false);
        }
        else
        {
            ShowFeedback("选择一套皮肤可查看全部光标角色。", isError: false);
        }

        UpdateSelection();
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateSelection()
    {
        var package = SkinsList.SelectedItem as CursorSkinPackage;
        DetailsPanel.DataContext = package;
        DetailsPanel.Visibility = package is null ? Visibility.Collapsed : Visibility.Visible;
        NoSelectionPanel.Visibility = package is null && _packages.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyButton.IsEnabled = package is not null && !package.IsApplied && _canApply;
        ApplyButton.Content = package?.IsApplied == true ? "正在使用" : "应用这套皮肤";
    }

    private void ShowFeedback(string message, bool isError)
    {
        StatusText.Text = message;
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(
            isError ? "MuDangerBrush" : "MuAccentBrush");
    }
}

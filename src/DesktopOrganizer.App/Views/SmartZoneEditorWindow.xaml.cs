using System.IO;
using System.Windows.Threading;
using DesktopOrganizer.Core.Models;
using DesktopOrganizer.Services;

namespace DesktopOrganizer.Views;

public partial class SmartZoneEditorWindow : System.Windows.Window
{
    private readonly SmartFolderCatalog _catalog = new();
    private readonly DispatcherTimer _previewTimer;
    private CancellationTokenSource? _previewCancellation;
    private bool _initializing = true;

    public SmartZoneEditorWindow(ZoneLayout? existing = null)
    {
        InitializeComponent();
        Existing = existing?.Clone();
        Presets =
        [
            new("全部文件", []),
            new("CAD", [".dwg", ".dxf", ".dwt"]),
            new("图片", [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"]),
            new("文档", [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt"]),
            new("自定义", []),
        ];
        SortOptions =
        [
            new(ZoneSortKind.ModifiedDescending, "最近修改"),
            new(ZoneSortKind.NameAscending, "文件名"),
            new(ZoneSortKind.SizeDescending, "文件大小"),
        ];
        PresetComboBox.ItemsSource = Presets;
        SortComboBox.ItemsSource = SortOptions;

        if (existing is not null)
        {
            ZoneNameTextBox.Text = existing.Name;
            SourcePathTextBox.Text = existing.SourcePath;
            IncludeSubfoldersCheckBox.IsChecked = existing.IncludeSubfolders;
            ShowHiddenFilesCheckBox.IsChecked = existing.ShowHiddenFiles;
            ExtensionsTextBox.Text = string.Join(", ", existing.Extensions);
            PresetComboBox.SelectedItem = FindMatchingPreset(existing.Extensions) ?? Presets[^1];
            SortComboBox.SelectedItem = SortOptions.First(option => option.Kind == existing.SortKind);
        }
        else
        {
            PresetComboBox.SelectedIndex = 0;
            SortComboBox.SelectedIndex = 0;
        }

        _previewTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, OnPreviewTimer, Dispatcher);
        _initializing = false;
        SchedulePreview();
    }

    public ZoneLayout? Existing { get; }

    public ZoneLayout? Result { get; private set; }

    private IReadOnlyList<FilterPreset> Presets { get; }

    private IReadOnlyList<SortOption> SortOptions { get; }

    private void OnBrowseClick(object sender, System.Windows.RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择智能分区的来源文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(SourcePathTextBox.Text) ? SourcePathTextBox.Text : string.Empty,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        SourcePathTextBox.Text = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(ZoneNameTextBox.Text))
        {
            ZoneNameTextBox.Text = BuildSuggestedName(dialog.SelectedPath, ParseExtensions());
        }
    }

    private void OnPresetChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing || PresetComboBox.SelectedItem is not FilterPreset preset || preset.Name == "自定义")
        {
            return;
        }

        ExtensionsTextBox.Text = string.Join(", ", preset.Extensions);
        SchedulePreview();
    }

    private void OnConfigurationChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_initializing)
        {
            SchedulePreview();
        }
    }

    private void SchedulePreview()
    {
        _previewTimer?.Stop();
        _previewTimer?.Start();
    }

    private async void OnPreviewTimer(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var layout = BuildLayout();
        if (!Directory.Exists(layout.SourcePath))
        {
            PreviewText.Text = "请选择一个可访问的来源文件夹";
            return;
        }

        PreviewText.Text = "正在后台扫描…";
        try
        {
            var result = await _catalog.ScanAsync(layout, cancellationToken: _previewCancellation.Token);
            PreviewText.Text = result.Error is not null
                ? result.Error
                : $"找到 {result.Entries.Count} 个匹配文件" + (result.IsLarge ? "；目录较大，建议关闭递归" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            // A newer preview request replaced this scan.
        }
    }

    private void OnSaveClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var layout = BuildLayout();
        if (string.IsNullOrWhiteSpace(layout.Name) || !Directory.Exists(layout.SourcePath))
        {
            System.Windows.MessageBox.Show(this, "请填写分区名称并选择有效的来源文件夹。", "分区设置不完整");
            return;
        }

        if (!SmartFolderCatalog.IsLocalFixedPath(layout.SourcePath))
        {
            System.Windows.MessageBox.Show(this, "首版仅支持本机固定磁盘中的文件夹。", "不支持此来源");
            return;
        }

        Result = layout;
        DialogResult = true;
    }

    private ZoneLayout BuildLayout()
    {
        var layout = Existing?.Clone() ?? new ZoneLayout
        {
            Kind = ZoneKind.SmartFolder,
            Width = 560,
            Height = 390,
        };
        layout.Kind = ZoneKind.SmartFolder;
        layout.IsInbox = false;
        layout.Name = ZoneNameTextBox.Text.Trim();
        layout.SourcePath = SourcePathTextBox.Text.Trim();
        layout.IncludeSubfolders = IncludeSubfoldersCheckBox.IsChecked == true;
        layout.ShowHiddenFiles = ShowHiddenFilesCheckBox.IsChecked == true;
        layout.Extensions = ParseExtensions();
        layout.SortKind = (SortComboBox.SelectedItem as SortOption)?.Kind ?? ZoneSortKind.ModifiedDescending;
        return layout;
    }

    private List<string> ParseExtensions() => ExtensionsTextBox.Text
        .Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(extension => extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private FilterPreset? FindMatchingPreset(IReadOnlyCollection<string> extensions) => Presets
        .Take(Presets.Count - 1)
        .FirstOrDefault(preset => preset.Extensions.Count == extensions.Count &&
            preset.Extensions.All(extension => extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)));

    private static string BuildSuggestedName(string sourcePath, IReadOnlyCollection<string> extensions)
    {
        var folder = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return extensions.Count == 0
            ? folder
            : $"{folder} · {string.Join(" ", extensions.Take(2).Select(extension => extension.TrimStart('.').ToUpperInvariant()))}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        base.OnClosed(e);
    }

    private sealed record FilterPreset(string Name, IReadOnlyList<string> Extensions);

    private sealed record SortOption(ZoneSortKind Kind, string Name);
}

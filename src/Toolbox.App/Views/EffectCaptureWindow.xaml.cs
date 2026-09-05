using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PersonalToolbox.Native;
using PersonalToolbox.Services;
using Toolbox.Core;

namespace PersonalToolbox.Views;

public partial class EffectCaptureWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private EffectCapturePackageResult? _result;
    private Func<EffectCapturePackageResult, CancellationToken, Task>? _send;

    public EffectCaptureWindow()
    {
        InitializeComponent();
        TaskbarWindowIdentity.Apply(this, TaskbarWindowIdentity.EffectCapture);
        Closed += (_, _) => _cancellation.Cancel();
    }

    public event EventHandler? CaptureAgainRequested;

    public CancellationToken CancellationToken => _cancellation.Token;

    public void SetProgress(string stage, double progress)
    {
        Dispatcher.Invoke(() =>
        {
            StageText.Text = stage;
            ProgressBar.Value = Math.Clamp(progress, 0, 1);
        });
    }

    public void ShowResult(
        EffectCapturePackageResult result,
        Func<EffectCapturePackageResult, CancellationToken, Task> send)
    {
        _result = result;
        _send = send;
        ProgressPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        CaptureAgainButton.Visibility = Visibility.Visible;
        SendButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        SubtitleText.Text = "关键变化已经按时间顺序展开，原始 MP4 也保留在本地素材包中。";
        ResultDetailText.Text = $"{result.Manifest.DurationMilliseconds} ms · {result.Manifest.LogicalWidth} × {result.Manifest.LogicalHeight} · {result.FramePaths.Count} 张关键帧";
        ContactSheetImage.Source = LoadImage(result.ContactSheetPath);
    }

    public void ShowFailure(string message)
    {
        StageText.Text = "这次没有完成";
        ProgressDetailText.Text = message;
        ProgressBar.Visibility = Visibility.Collapsed;
        CancelButton.Content = "关闭";
        CaptureAgainButton.Visibility = Visibility.Visible;
    }

    private async void Send_OnClick(object sender, RoutedEventArgs e)
    {
        if (_result is null || _send is null)
        {
            return;
        }

        SendButton.IsEnabled = false;
        CaptureAgainButton.IsEnabled = false;
        SendStatusText.Text = "正在连接 Codex…";
        try
        {
            await _send(_result, _cancellation.Token);
            SendStatusText.Text = "已在 Codex 中准备好";
            ResultTitleText.Text = "素材已交给 Codex";
            HintText.Text = "Codex 已打开素材目录并预填分析提示；请在输入框中按发送。";
            SendButton.Content = "再次打开 Codex";
            SendButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            SendStatusText.Text = "已取消";
            SendButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            SendStatusText.Text = "发送失败";
            HintText.Text = exception.Message;
            SendButton.IsEnabled = true;
        }
        finally
        {
            CaptureAgainButton.IsEnabled = true;
        }
    }

    private void CaptureAgain_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        CaptureAgainRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        _cancellation.Cancel();
        Close();
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var path = _result?.RootPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_result!.ContactSheetPath}\"") { UseShellExecute = true });
    }

    private static BitmapImage LoadImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

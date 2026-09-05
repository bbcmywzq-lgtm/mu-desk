using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using PersonalToolbox.Modules;
using Toolbox.Core;
using MediaColor = System.Windows.Media.Color;

namespace EffectCapture.TestApp;

public partial class MainWindow : Window
{
    private readonly EffectCaptureModule _capture;
    private readonly string _defaultOutputRoot;

    public MainWindow(EffectCaptureModule capture, EffectCaptureSettings settings, string defaultOutputRoot)
    {
        InitializeComponent();
        _capture = capture;
        Settings = settings;
        _defaultOutputRoot = defaultOutputRoot;
    }

    public EffectCaptureSettings Settings { get; }

    public void SetCaptureState(bool busy)
    {
        StartButton.IsEnabled = !busy;
        SetStatus(busy ? "正在录制或整理素材…" : "素材已生成；可在结果窗口中检查并在 Codex 中打开。", isError: false);
    }

    public void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(isError
            ? MediaColor.FromRgb(168, 66, 74)
            : MediaColor.FromRgb(38, 118, 80));
    }

    private void Start_OnClick(object sender, RoutedEventArgs e) => _capture.StartCapture();

    private void Settings_OnClick(object sender, RoutedEventArgs e) => _capture.OpenSettings(this);

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var root = string.IsNullOrWhiteSpace(Settings.OutputRoot)
            ? _defaultOutputRoot
            : Path.GetFullPath(Settings.OutputRoot);
        Directory.CreateDirectory(root);
        Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
    }

}

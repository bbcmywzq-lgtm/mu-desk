using System.Windows;
using System.Windows.Interop;
using PersonalToolbox.Native;
using PersonalToolbox.Services;

namespace PersonalToolbox.Views;

public partial class EffectCaptureHudWindow : Window
{
    public EffectCaptureHudWindow(EffectCaptureRegion region)
    {
        InitializeComponent();
        Width = 310;
        Height = 48;
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            var transform = source.CompositionTarget.TransformToDevice;
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(region.X, region.Y));
            var logicalWidth = Width;
            var below = (region.Y + region.Height + 10) / transform.M22;
            var above = (region.Y - 58) / transform.M22;
            var workTop = screen.WorkingArea.Top / transform.M22;
            var workBottom = screen.WorkingArea.Bottom / transform.M22;
            Left = Math.Clamp(region.X / transform.M11, screen.WorkingArea.Left / transform.M11, screen.WorkingArea.Right / transform.M11 - logicalWidth);
            Top = below + Height <= workBottom ? below : Math.Max(workTop, above);
            CaptureInterop.ExcludeWindowFromCapture(source.Handle);
        };
    }

    public void SetText(string text) => StatusText.Text = text;

    public void SetRecording(bool recording) => ActionPanel.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;

    public event EventHandler? StopRequested;

    public event EventHandler? CancelRequested;

    private void Stop_OnClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);
}

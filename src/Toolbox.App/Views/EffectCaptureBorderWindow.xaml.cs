using System.Windows;
using System.Windows.Interop;
using PersonalToolbox.Native;
using PersonalToolbox.Services;

namespace PersonalToolbox.Views;

public partial class EffectCaptureBorderWindow : Window
{
    public EffectCaptureBorderWindow(EffectCaptureRegion region)
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            var transform = source.CompositionTarget.TransformToDevice;
            Left = region.X / transform.M11;
            Top = region.Y / transform.M22;
            Width = region.Width / transform.M11;
            Height = region.Height / transform.M22;
            CaptureInterop.ExcludeWindowFromCapture(source.Handle);
            CaptureInterop.MakeWindowClickThrough(source.Handle);
        };
    }
}

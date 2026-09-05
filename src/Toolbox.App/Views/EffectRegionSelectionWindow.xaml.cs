using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PersonalToolbox.Native;
using PersonalToolbox.Services;
using Forms = System.Windows.Forms;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfCanvas = System.Windows.Controls.Canvas;

namespace PersonalToolbox.Views;

public sealed record EffectRegionSelectionResult(EffectCaptureRegion Region, int DurationSeconds);

public partial class EffectRegionSelectionWindow : Window
{
    private readonly Forms.Screen _screen;
    private WpfPoint _start;
    private Rect _selection;
    private bool _dragging;
    private double _scaleX = 1;
    private double _scaleY = 1;

    public EffectRegionSelectionWindow(int defaultDurationSeconds)
    {
        InitializeComponent();
        var cursor = Forms.Cursor.Position;
        _screen = Forms.Screen.FromPoint(cursor);
        DurationBox.SelectedIndex = defaultDurationSeconds switch { 5 => 1, 7 => 2, _ => 0 };
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => ResetToIdle();
    }

    public EffectRegionSelectionResult? Result { get; private set; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        var transform = source.CompositionTarget.TransformToDevice;
        _scaleX = transform.M11;
        _scaleY = transform.M22;
        Left = _screen.Bounds.Left / _scaleX;
        Top = _screen.Bounds.Top / _scaleY;
        Width = _screen.Bounds.Width / _scaleX;
        Height = _screen.Bounds.Height / _scaleY;
        CaptureInterop.ExcludeWindowFromCapture(source.Handle);
    }

    private void Canvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Toolbar.IsMouseOver)
        {
            return;
        }

        _dragging = true;
        _start = Clamp(e.GetPosition(RootCanvas));
        _selection = new Rect(_start, _start);
        Toolbar.Visibility = Visibility.Collapsed;
        InstructionPanel.Visibility = Visibility.Collapsed;
        SelectionBorder.Visibility = Visibility.Visible;
        RootCanvas.CaptureMouse();
        UpdateSelection();
    }

    private void Canvas_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var current = Clamp(e.GetPosition(RootCanvas));
        _selection = new Rect(_start, current);
        UpdateSelection();
    }

    private void Canvas_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        RootCanvas.ReleaseMouseCapture();
        if (_selection.Width < 24 || _selection.Height < 24)
        {
            Reselect();
            return;
        }

        PositionToolbar();
        Toolbar.Visibility = Visibility.Visible;
    }

    private void UpdateSelection()
    {
        WpfCanvas.SetLeft(SelectionBorder, _selection.Left);
        WpfCanvas.SetTop(SelectionBorder, _selection.Top);
        SelectionBorder.Width = _selection.Width;
        SelectionBorder.Height = _selection.Height;

        SetRectangle(ShadeTop, 0, 0, ActualWidth, _selection.Top);
        SetRectangle(ShadeLeft, 0, _selection.Top, _selection.Left, _selection.Height);
        SetRectangle(ShadeRight, _selection.Right, _selection.Top, Math.Max(0, ActualWidth - _selection.Right), _selection.Height);
        SetRectangle(ShadeBottom, 0, _selection.Bottom, ActualWidth, Math.Max(0, ActualHeight - _selection.Bottom));
    }

    private void PositionToolbar()
    {
        Toolbar.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var size = Toolbar.DesiredSize;
        var x = Math.Clamp(_selection.Left, 8, Math.Max(8, ActualWidth - size.Width - 8));
        var below = _selection.Bottom + 10;
        var y = below + size.Height <= ActualHeight ? below : Math.Max(8, _selection.Top - size.Height - 10);
        WpfCanvas.SetLeft(Toolbar, x);
        WpfCanvas.SetTop(Toolbar, y);
    }

    private void Start_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selection.Width < 24 || _selection.Height < 24)
        {
            return;
        }

        var seconds = DurationBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                      int.TryParse(item.Tag?.ToString(), out var parsed) ? parsed : 2;
        var physicalX = _screen.Bounds.Left + (int)Math.Round(_selection.Left * _scaleX);
        var physicalY = _screen.Bounds.Top + (int)Math.Round(_selection.Top * _scaleY);
        var physicalWidth = Math.Min(_screen.Bounds.Right - physicalX, Math.Max(2, (int)Math.Round(_selection.Width * _scaleX)));
        var physicalHeight = Math.Min(_screen.Bounds.Bottom - physicalY, Math.Max(2, (int)Math.Round(_selection.Height * _scaleY)));
        Result = new EffectRegionSelectionResult(
            new EffectCaptureRegion(
                CaptureInterop.MonitorFromPhysicalPoint(physicalX, physicalY),
                _screen.Bounds.Left,
                _screen.Bounds.Top,
                physicalX,
                physicalY,
                physicalWidth,
                physicalHeight),
            seconds);
        DialogResult = true;
    }

    private void Reselect_OnClick(object sender, RoutedEventArgs e) => Reselect();

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void Reselect()
    {
        SelectionBorder.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = Visibility.Collapsed;
        InstructionPanel.Visibility = Visibility.Visible;
        _selection = Rect.Empty;
        ResetToIdle();
    }

    private void ResetToIdle()
    {
        SetRectangle(ShadeTop, 0, 0, 0, 0);
        SetRectangle(ShadeLeft, 0, 0, 0, 0);
        SetRectangle(ShadeRight, 0, 0, 0, 0);
        SetRectangle(ShadeBottom, 0, 0, 0, 0);
    }

    private WpfPoint Clamp(WpfPoint point) => new(
        Math.Clamp(point.X, 0, ActualWidth),
        Math.Clamp(point.Y, 0, ActualHeight));

    private static void SetRectangle(FrameworkElement element, double x, double y, double width, double height)
    {
        WpfCanvas.SetLeft(element, x);
        WpfCanvas.SetTop(element, y);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }
}

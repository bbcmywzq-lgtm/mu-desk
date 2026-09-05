using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PersonalToolbox.Native;
using Forms = System.Windows.Forms;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace PersonalToolbox.Views;

public partial class CueRegionSelectionWindow : Window
{
    private readonly Forms.Screen _screen;
    private WpfPoint _start;
    private Rect _selection;
    private bool _dragging;
    private double _scaleX = 1;
    private double _scaleY = 1;

    public CueRegionSelectionWindow()
    {
        InitializeComponent();
        _screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        SourceInitialized += OnSourceInitialized;
    }

    public System.Drawing.Rectangle? SelectedRegion { get; private set; }

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
    }

    private void Canvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(RootCanvas);
        _selection = new Rect(_start, _start);
        Selection.Visibility = Visibility.Visible;
        Hint.Visibility = Visibility.Collapsed;
        RootCanvas.CaptureMouse();
    }

    private void Canvas_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _selection = new Rect(_start, e.GetPosition(RootCanvas));
        Canvas.SetLeft(Selection, _selection.Left);
        Canvas.SetTop(Selection, _selection.Top);
        Selection.Width = _selection.Width;
        Selection.Height = _selection.Height;
    }

    private void Canvas_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        RootCanvas.ReleaseMouseCapture();
        if (_selection.Width < 8 || _selection.Height < 8)
        {
            Selection.Visibility = Visibility.Collapsed;
            Hint.Visibility = Visibility.Visible;
            return;
        }

        SelectedRegion = new System.Drawing.Rectangle(
            _screen.Bounds.Left + (int)Math.Round(_selection.Left * _scaleX),
            _screen.Bounds.Top + (int)Math.Round(_selection.Top * _scaleY),
            Math.Max(1, (int)Math.Round(_selection.Width * _scaleX)),
            Math.Max(1, (int)Math.Round(_selection.Height * _scaleY)));
        DialogResult = true;
    }

    private void Window_OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}

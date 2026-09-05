using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Diagnostics;
using PersonalToolbox.Native;
using PersonalToolbox.Services;
using Toolbox.Core;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;

namespace PersonalToolbox.Views;

internal sealed class CueOverlayWindow : Window
{
    private readonly CueOverlayVisual _visual;

    public CueOverlayWindow(CueSettings settings)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        _visual = new CueOverlayVisual(settings, this);
        Content = _visual;
        SourceInitialized += OnSourceInitialized;
    }

    public void AddClick(System.Drawing.Point point) => _visual.AddClick(point);

    public void Refresh() => _visual.RefreshState();

    public void StopRendering() => _visual.Stop();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        var transform = source.CompositionTarget.TransformToDevice;
        var virtualBounds = GetVirtualBounds();
        Left = virtualBounds.Left / transform.M11;
        Top = virtualBounds.Top / transform.M22;
        Width = virtualBounds.Width / transform.M11;
        Height = virtualBounds.Height / transform.M22;
        CaptureInterop.MakeWindowClickThrough(source.Handle);
        CaptureInterop.ExcludeWindowFromCapture(source.Handle);
    }

    private static System.Drawing.Rectangle GetVirtualBounds() =>
        Forms.Screen.AllScreens.Select(screen => screen.Bounds).Aggregate(System.Drawing.Rectangle.Union);
}

internal sealed class CueOverlayVisual : FrameworkElement
{
    private readonly CueSettings _settings;
    private readonly Window _window;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CueLaserStroke _laser = new();
    private bool _rendering;
    private readonly List<(System.Drawing.Point Point, DateTime Time)> _pulses = [];
    private System.Drawing.Point _cursor;


    public CueOverlayVisual(CueSettings settings, Window window)
    {
        _settings = settings;
        _window = window;
        IsHitTestVisible = false;
        window.IsVisibleChanged += (_, _) =>
        {
            if (window.IsVisible && !_rendering)
            {
                _rendering = true;
                Tick();
                CompositionTarget.Rendering += RenderFrame;
            }
            else if (!window.IsVisible) Stop();
        };
        window.Closed += (_, _) => Stop();
    }

    public void AddClick(System.Drawing.Point point)
    {
        if (_settings.ClickPulseEnabled)
        {
            _pulses.Add((point, DateTime.UtcNow));
            InvalidateVisual();
        }
    }

    public void RefreshState()
    {
        if (!_settings.LaserEnabled) _laser.Clear();
        Tick();
        InvalidateVisual();
    }

    public void Stop()
    {
        CompositionTarget.Rendering -= RenderFrame;
        _rendering = false;
        _laser.Clear();
    }

    private void RenderFrame(object? sender, EventArgs e) => Tick();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var pointer = ToLocal(_cursor);
        if (_settings.SpotlightEnabled)
        {
            DrawSpotlight(drawingContext, pointer);
        }

        if (_settings.LaserEnabled)
        {
            _laser.Draw(drawingContext, _clock.Elapsed.TotalSeconds);
        }

        if (_settings.PointerRingEnabled)
        {
            drawingContext.DrawEllipse(
                WpfBrushes.Transparent,
                new WpfPen(new SolidColorBrush(WpfColor.FromArgb(230, 128, 85, 217)), 3),
                pointer,
                24,
                24);
        }

        DrawPulses(drawingContext);
    }

    private void Tick()
    {
        var nextCursor = Forms.Cursor.Position;
        var moved = nextCursor != _cursor;
        _cursor = nextCursor;
        var now = DateTime.UtcNow;
        var hadTrail = _laser.SampleCount > 1;
        var hadPulses = _pulses.Count > 0;
        if (_settings.LaserEnabled) _laser.Update(ToLocal(_cursor), _clock.Elapsed.TotalSeconds);
        else _laser.Clear();
        _pulses.RemoveAll(item => now - item.Time > TimeSpan.FromMilliseconds(430));
        if (moved || hadTrail || hadPulses) InvalidateVisual();
    }

    private void DrawSpotlight(DrawingContext context, WpfPoint center)
    {
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var radius = _settings.SpotlightRadius;
        Geometry opening = _settings.SpotlightShape == "RoundedRectangle"
            ? new RectangleGeometry(new Rect(center.X - radius * 1.35, center.Y - radius * 0.72, radius * 2.7, radius * 1.44), 22, 22)
            : new EllipseGeometry(center, radius, radius);
        var mask = new CombinedGeometry(GeometryCombineMode.Exclude, full, opening);
        var alpha = (byte)Math.Clamp((int)Math.Round(_settings.SpotlightOpacity * 255), 0, 255);
        context.DrawGeometry(new SolidColorBrush(WpfColor.FromArgb(alpha, 12, 14, 20)), null, mask);
    }

    private void DrawPulses(DrawingContext context)
    {
        var now = DateTime.UtcNow;
        foreach (var pulse in _pulses)
        {
            var progress = Math.Clamp((now - pulse.Time).TotalMilliseconds / 430, 0, 1);
            var radius = 14 + progress * 28;
            var alpha = (byte)((1 - progress) * 220);
            context.DrawEllipse(
                WpfBrushes.Transparent,
                new WpfPen(new SolidColorBrush(WpfColor.FromArgb(alpha, 128, 85, 217)), 3),
                ToLocal(pulse.Point),
                radius,
                radius);
        }
    }

    private WpfPoint ToLocal(System.Drawing.Point point)
    {
        try
        {
            return _window.PointFromScreen(new WpfPoint(point.X, point.Y));
        }
        catch (InvalidOperationException)
        {
            return new WpfPoint();
        }
    }


}

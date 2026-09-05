using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PersonalToolbox.Native;
using PersonalToolbox.Services;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using TextBox = System.Windows.Controls.TextBox;
using Canvas = System.Windows.Controls.Canvas;
using FontFamily = System.Windows.Media.FontFamily;

namespace PersonalToolbox.Views;

public partial class CueAnnotationWindow : Window
{
    internal CueAnnotationDocument Document { get; }
    private readonly AnnotationSurface _surface;
    private readonly List<Point> _points = [];
    private readonly System.Drawing.Rectangle _bounds;
    private CueMark? _draft, _editing;
    private TextBox? _editor;
    private BitmapSource? _frozen;
    private bool _active, _closed;
    private Color _color = Color.FromRgb(157, 113, 238), _highlightColor = Color.FromRgb(255, 211, 88);
    private double _lineSize = 4, _highlightSize = 20, _fontSize = 22;
    internal Window? ToolWindow { get; set; }
    public string Tool { get; private set; } = "Pen";
    public string LastShape { get; private set; } = "Arrow";
    public bool IsFrozen => _frozen is not null;
    public bool IsOperatingDesktop { get; private set; }
    public bool IsBusy { get; private set; }
    public Color CurrentColor => Tool == "Highlighter" ? _highlightColor : _color;
    public double CurrentSize => Tool == "Text" ? _fontSize : Tool == "Highlighter" ? _highlightSize : _lineSize;
    public event Action? AnnotationStateChanged;
    public event Action? ExitRequested;

    public CueAnnotationWindow() : this(new CueAnnotationDocument()) { }

    internal CueAnnotationWindow(CueAnnotationDocument document)
    {
        Document = document;
        InitializeComponent();
        _bounds = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        _surface = new AnnotationSurface(this); SurfaceHost.Children.Add(_surface);
        _surface.MouseLeftButtonDown += PointerDown; _surface.MouseMove += PointerMove; _surface.MouseLeftButtonUp += PointerUp;
        _surface.LostMouseCapture += (_, _) => { if (_draft is not null) CancelGesture(); };
        Document.Changed += Changed;
        SourceInitialized += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = _bounds.Left / dpi.DpiScaleX; Top = _bounds.Top / dpi.DpiScaleY;
            Width = _bounds.Width / dpi.DpiScaleX; Height = _bounds.Height / dpi.DpiScaleY;
            CaptureInterop.ExcludeWindowFromCapture(new WindowInteropHelper(this).Handle);
        };
        Closed += (_, _) => { _closed = true; _active = false; Document.Changed -= Changed; };
    }
    public void Resume() { _active = true; Show(); SetDesktop(false); Activate(); Changed(); }
    public void Suspend()
    {
        CommitText(); CancelGesture(); _active = false; Hide();
        _frozen = null; FrozenImage.Source = null; FrozenImage.Visibility = Visibility.Collapsed;
        IsOperatingDesktop = false; Changed();
    }
    public void SelectTool(string tool)
    {
        CommitText(); CancelGesture(); SetDesktop(false); Tool = tool;
        if (tool is "Line" or "Arrow" or "Rectangle" or "Ellipse") LastShape = tool;
        Changed();
    }
    public void SetColor(Color color) { CommitText(); if (Tool == "Highlighter") _highlightColor = color; else _color = color; Changed(); }
    public void SetSize(double size)
    {
        CommitText();
        if (Tool == "Text") _fontSize = size; else if (Tool == "Highlighter") _highlightSize = size; else _lineSize = size;
        Changed();
    }
    public void Undo() { CommitText(); CancelGesture(); Document.Undo(); }
    public void Redo() { CommitText(); CancelGesture(); Document.Redo(); }
    public void ClearMarks() { CommitText(); CancelGesture(); Document.Clear(); }
    public void Escape()
    {
        if (_editor is not null) { CancelText(); return; }
        if (_draft is not null) { CancelGesture(); return; }
        ExitRequested?.Invoke();
    }
    public void ToggleDesktop() { CommitText(); CancelGesture(); SetDesktop(!IsOperatingDesktop); }
    private void SetDesktop(bool enabled)
    {
        if (enabled && IsFrozen) { _frozen = null; FrozenImage.Source = null; FrozenImage.Visibility = Visibility.Collapsed; }
        IsOperatingDesktop = enabled;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) CaptureInterop.SetWindowClickThrough(handle, enabled);
        if (!enabled && _active) Activate();
        Changed();
    }
    public async Task ToggleFreezeAsync()
    {
        if (IsBusy) return;
        CommitText(); CancelGesture();
        if (IsFrozen) { _frozen = null; FrozenImage.Source = null; FrozenImage.Visibility = Visibility.Collapsed; Changed(); return; }
        SetDesktop(false); IsBusy = true; Changed();
        try
        {
            var image = await CaptureBackgroundAsync();
            if (!_active || _closed) return;
            _frozen = image; FrozenImage.Source = image; FrozenImage.Visibility = Visibility.Visible;
        }
        finally { IsBusy = false; Changed(); }
    }
    public async Task CopyAsync()
    {
        if (IsBusy) return; IsBusy = true; Changed();
        try { System.Windows.Clipboard.SetImage(await CompositeAsync()); }
        finally { IsBusy = false; Changed(); }
    }
    public async Task SaveAsync()
    {
        if (IsBusy) return;
        CommitText();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"Cue-{DateTime.Now:yyyyMMdd-HHmmss}.png", DefaultExt = ".png" };
        if (dialog.ShowDialog(ToolWindow ?? this) != true) return;
        IsBusy = true; Changed();
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(await CompositeAsync()));
            using var stream = File.Create(dialog.FileName); encoder.Save(stream);
        }
        finally { IsBusy = false; Changed(); }
    }
    internal async Task<BitmapSource> CompositeAsync()
    {
        CommitText(); CancelGesture(); var background = _frozen ?? await CaptureBackgroundAsync();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) { dc.DrawImage(background, new Rect(0, 0, ActualWidth, ActualHeight)); DrawMarks(dc); }
        var result = new RenderTargetBitmap(background.PixelWidth, background.PixelHeight,
            96 * background.PixelWidth / ActualWidth, 96 * background.PixelHeight / ActualHeight, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
    private async Task<BitmapSource> CaptureBackgroundAsync()
    {
        var toolbarWasVisible = ToolWindow?.IsVisible == true;
        ToolWindow?.Hide(); Hide();
        try
        {
            await Task.Delay(70); // DWM must remove both surfaces before the one-shot capture.
            using var bitmap = new System.Drawing.Bitmap(_bounds.Width, _bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(_bounds.Location, System.Drawing.Point.Empty, _bounds.Size);
            var handle = bitmap.GetHbitmap();
            try
            {
                var result = Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                result.Freeze(); return result;
            }
            finally { DeleteObject(handle); }
        }
        finally { if (_active && !_closed) { Show(); if (!IsOperatingDesktop) Activate(); if (toolbarWasVisible) ToolWindow?.Show(); } }
    }
    private void PointerDown(object sender, MouseButtonEventArgs e)
    {
        BeginGesture(e.GetPosition(_surface), e.ClickCount); e.Handled = true;
    }
    internal void BeginGesture(Point point, int clickCount = 1)
    {
        if (IsOperatingDesktop || IsBusy) return;
        CommitText();
        var text = Document.Items.LastOrDefault(x => x.Tool == "Text" && CueMarkDrawing.Hit(x, point));
        if (text is not null && Tool != "Erase")
        {
            if (clickCount == 2 || Tool == "Text") BeginText(text);
            return;
        }
        if (Tool == "Erase")
        {
            var hit = Document.Items.LastOrDefault(x => CueMarkDrawing.Hit(x, point));
            if (hit is not null) Document.Remove(hit.Id);
            return;
        }
        if (Tool == "Text") { BeginText(NewMark([point])); return; }
        if (Tool == "Step") { Document.Add(NewMark([point]) with { Number = Document.NextNumber }); return; }
        _points.Clear(); _points.Add(point); _draft = NewMark([point, point]);
        _surface.CaptureMouse(); _surface.InvalidateVisual();
    }
    private CueMark NewMark(Point[] points) => new(Guid.NewGuid(), Tool, points, CurrentColor, CurrentSize);
    private void PointerMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_draft is null || e.LeftButton != MouseButtonState.Pressed) return;
        MoveGesture(e.GetPosition(_surface));
    }
    internal void MoveGesture(Point point)
    {
        if (_draft is null) return;
        point = new Point(Math.Clamp(point.X, 0, ActualWidth), Math.Clamp(point.Y, 0, ActualHeight));
        if (Tool is "Pen" or "Highlighter")
        {
            if ((point - _points[^1]).Length < .7) return;
            _points.Add(point); _draft = _draft with { Points = _points.ToArray() };
        }
        else _draft = _draft with { Points = [_points[0], point] };
        _surface.InvalidateVisual();
    }
    private void PointerUp(object sender, MouseButtonEventArgs e)
    {
        MoveGesture(e.GetPosition(_surface)); EndGesture(); e.Handled = true;
    }
    internal void EndGesture()
    {
        if (_draft is null) return;
        var finished = _draft; _draft = null; _surface.ReleaseMouseCapture();
        if (Tool is "Pen" or "Highlighter" || (finished.Points[^1] - finished.Points[0]).Length >= 2) Document.Add(finished);
        _surface.InvalidateVisual();
    }
    private void CancelGesture() { _draft = null; _points.Clear(); _surface.ReleaseMouseCapture(); _surface.InvalidateVisual(); }
    private void BeginText(CueMark mark)
    {
        _editing = mark;
        _editor = new TextBox { Text = mark.Text, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = mark.Size,
            Foreground = new SolidColorBrush(mark.Color), Background = new SolidColorBrush(Color.FromArgb(235, 35, 33, 43)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(165, 132, 226)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6), MinWidth = 160, MaxWidth = Math.Max(160, ActualWidth - mark.Points[0].X - 12),
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        Canvas.SetLeft(_editor, Math.Min(mark.Points[0].X, Math.Max(0, ActualWidth - 180)));
        Canvas.SetTop(_editor, Math.Min(mark.Points[0].Y, Math.Max(0, ActualHeight - 80)));
        _editing = mark with { Points = [new Point(Canvas.GetLeft(_editor), Canvas.GetTop(_editor))] };
        if (mark.TextWidth > 0) _editor.Width = mark.TextWidth + 14;
        var editor = _editor;
        EditorLayer.Children.Add(editor);
        editor.LostKeyboardFocus += (_, _) => { if (ReferenceEquals(_editor, editor)) CommitText(); };
        editor.Focus(); editor.SelectAll(); _surface.InvalidateVisual();
    }
    private void CommitText()
    {
        if (_editor is null || _editing is null) return;
        _editor.UpdateLayout();
        var item = _editing with { Text = _editor.Text.Trim(), TextWidth = Math.Max(1, _editor.ActualWidth - 14) }; CancelText();
        if (string.IsNullOrWhiteSpace(item.Text)) { Document.Remove(item.Id); return; }
        if (Document.Items.Any(x => x.Id == item.Id)) Document.Replace(item); else Document.Add(item);
    }
    private void CancelText() { var editor = _editor; _editor = null; _editing = null; if (editor is not null) EditorLayer.Children.Remove(editor); _surface.InvalidateVisual(); }
    private void Changed() { _surface.InvalidateVisual(); AnnotationStateChanged?.Invoke(); }
    private void DrawMarks(DrawingContext dc)
    {
        foreach (var item in Document.Items) if (item.Id != _editing?.Id) CueMarkDrawing.Draw(dc, item);
        if (_draft is not null) CueMarkDrawing.Draw(dc, _draft);
    }
    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Escape(); e.Handled = true; return; }
        if (_editor is not null)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { CommitText(); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.Z) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Redo(); else Undo(); e.Handled = true; }
            if (e.Key == Key.Y) { Redo(); e.Handled = true; }
        }
    }
    private sealed class AnnotationSurface(CueAnnotationWindow owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc) { dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); owner.DrawMarks(dc); }
    }
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
}

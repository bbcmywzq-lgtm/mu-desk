using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PersonalToolbox.Services;
using PersonalToolbox.Modules;
using PersonalToolbox.Views;
using Toolbox.Core;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--reset-native"))
        {
            if (!MagInitialize()) throw new Exception("Cannot initialize recovery.");
            try
            {
                if (!MagSetFullscreenTransform(1, 0, 0) || !MagGetFullscreenTransform(out var z, out var x, out var y) || z != 1 || x != 0 || y != 0)
                    throw new Exception("Native magnification recovery failed.");
                Console.WriteLine("Native transform recovered: 1x, offset 0,0.");
            }
            finally { MagUninitialize(); }
            return;
        }
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            var point = System.Windows.Forms.Cursor.Position;
            var window = new Window { Title = "Cue rendering probe", Width = 600, Height = 400,
                Left = Math.Max(0, point.X - 300), Top = Math.Max(0, point.Y - 200),
                ShowActivated = false, Content = new MotionPattern() };
            CueGpuMagnifier? lens = null;
            CueFocusController? focus = null;
            var focusIntervals = new List<double>();
            var focusErrors = new List<string>();
            var previousFocus = 0L;
            try
            {
                if (args.Contains("--names"))
                {
                    await VerifyModuleNames();
                    return;
                }
                if (args.Contains("--drop"))
                {
                    await VerifyDropShelf();
                    return;
                }
                if (args.Contains("--maple-hand"))
                {
                    BuildMapleHand(args.Contains("--bold"));
                    return;
                }
                if (args.Contains("--toolbar-motion"))
                {
                    await MeasureToolbarMotion();
                    return;
                }
                if (args.Contains("--annotation"))
                {
                    await VerifyAnnotation();
                    return;
                }
                if (args.Contains("--laser"))
                {
                    await VerifyLaser();
                    return;
                }
                if (args.Contains("--toolbar"))
                {
                    await VerifyToolbar();
                    return;
                }
                window.Show();
                if (args.Contains("--lifecycle"))
                {
                    await MeasureLifecycle();
                    return;
                }
                if (args.Contains("--legacy"))
                {
                    await MeasureLegacy();
                    return;
                }
                var settings = new CueSettings { MagnifierEnabled = true };
                lens = new CueGpuMagnifier(settings);
                lens.DiagnosticFullScreen = args.Contains("--gpu-focus");
                if (args.Contains("--snapshot")) lens.DiagnosticSnapshotPath = System.IO.Path.GetFullPath("artifacts/cue-gpu-probe.png");
                lens.Start();
                await Task.Delay(args.Contains("--soak") ? 30000 : 6000);
                if (args.Contains("--focus"))
                {
                    focus = new CueFocusController(settings, () => { });
                    focus.ZoomChanged += _ =>
                    {
                        var now = Stopwatch.GetTimestamp();
                        if (previousFocus != 0) focusIntervals.Add(Stopwatch.GetElapsedTime(previousFocus, now).TotalMilliseconds);
                        previousFocus = now;
                    };
                    focus.Error += exception => focusErrors.Add(exception.ToString());
                    focus.Begin(); await Task.Delay(80);
                    focus.End(); await Task.Delay(50);
                    focus.Begin(); await Task.Delay(350);
                    focus.AdjustZoom(1); await Task.Delay(300);
                    focus.End(); await Task.Delay(1200);
                    focus.Dispose(); focus = null;
                }
                lens.Dispose();
                Console.WriteLine(JsonSerializer.Serialize(lens.Statistics, new JsonSerializerOptions { WriteIndented = true }));
                if (args.Contains("--focus"))
                {
                    focusIntervals.Sort();
                    if (!MagInitialize()) throw new Exception("Cannot initialize native verification.");
                    var queried = MagGetFullscreenTransform(out var zoom, out var x, out var y);
                    MagUninitialize();
                    Console.WriteLine(JsonSerializer.Serialize(new { NativeFocus = new {
                        Samples = focusIntervals.Count,
                        MedianMs = focusIntervals.Count > 0 ? focusIntervals[focusIntervals.Count / 2] : 0,
                        P95Ms = focusIntervals.Count > 0 ? focusIntervals[(int)((focusIntervals.Count - 1) * .95)] : 0,
                        Restored = queried && zoom == 1 && x == 0 && y == 0, QuerySucceeded = queried,
                        Zoom = zoom, X = x, Y = y, Errors = focusErrors } }));
                    if (!queried || zoom != 1 || x != 0 || y != 0 || focusErrors.Count != 0) throw new Exception("Native focus verification failed.");
                }
                Environment.ExitCode = lens.Failure is null ? 0 : 1;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
            finally { focus?.Dispose(); lens?.Dispose(); window.Close(); app.Shutdown(); }
        };
        app.Run();
    }
    [DllImport("Magnification.dll")] private static extern bool MagGetFullscreenTransform(out float zoom, out int x, out int y);
    [DllImport("Magnification.dll")] private static extern bool MagInitialize();
    [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] private static extern bool MagSetFullscreenTransform(float zoom, int x, int y);
    private static async Task MeasureToolbarMotion()
    {
        using var module = new CueModule(new CueSettings { ClickPulseEnabled = false }, () => { });
        var toolbar = new CueToolbarWindow(module);
        var intervals = new List<double>(); var starts = new List<double>();
        var surface = (System.Windows.Controls.Border)toolbar.FindName("ExpandedSurface");
        var content = surface.Child; surface.Child = null;
        var contentLayout = new LayoutCounter { Child = content }; surface.Child = contentLayout;
        var contentMeasures = 0; var contentArranges = 0;
        var windowChanges = 0; var layouts = 0; var frames = 0;
        var recording = false; var previous = 0L; var began = 0L;
        TimeSpan lastRendering = TimeSpan.MinValue;
        void Frame(object? sender, EventArgs e)
        {
            if (!recording || e is not RenderingEventArgs rendering || rendering.RenderingTime == lastRendering) return;
            lastRendering = rendering.RenderingTime;
            var now = Stopwatch.GetTimestamp(); frames++;
            if (previous != 0) intervals.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
            else starts.Add(Stopwatch.GetElapsedTime(began, now).TotalMilliseconds);
            previous = now;
        }
        void Layout(object? sender, EventArgs e) { if (recording) layouts++; }
        nint Hook(nint h, int message, nint w, nint l, ref bool handled) { if (recording && message == 0x47) windowChanges++; return 0; }
        try
        {
            toolbar.CollapseToNotch(); await Task.Delay(200);
            var source = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(toolbar);
            source.AddHook(Hook); toolbar.LayoutUpdated += Layout; CompositionTarget.Rendering += Frame;
            for (var i = 0; i < 8; i++)
            {
                foreach (var expand in new[] { true, false })
                {
                    previous = 0; began = Stopwatch.GetTimestamp(); recording = true;
                    var measuresBefore = contentLayout.Measures; var arrangesBefore = contentLayout.Arranges;
                    if (expand) toolbar.Expand(); else toolbar.CollapseToNotch();
                    await Task.Delay(470); recording = false;
                    contentMeasures += contentLayout.Measures - measuresBefore; contentArranges += contentLayout.Arranges - arrangesBefore;
                    await Task.Delay(40);
                }
            }
            intervals.Sort(); starts.Sort();
            var result = JsonSerializer.Serialize(new { Probe = "WPF toolbar motion callbacks (not displayed FPS)", Transitions = 16,
                Frames = frames, NativeWindowChanges = windowChanges, LayoutNotifications = layouts,
                ToolbarContentMeasures = contentMeasures, ToolbarContentArranges = contentArranges,
                MedianFrameIntervalMs = intervals.Count == 0 ? 0 : intervals[intervals.Count / 2],
                P95FrameIntervalMs = intervals.Count == 0 ? 0 : intervals[(int)((intervals.Count - 1) * .95)],
                MedianFirstCallbackMs = starts.Count == 0 ? 0 : starts[starts.Count / 2] }, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(result);
        }
        finally { CompositionTarget.Rendering -= Frame; toolbar.LayoutUpdated -= Layout; toolbar.Close(); }
    }
    private sealed class LayoutCounter : System.Windows.Controls.Decorator
    {
        internal int Measures, Arranges;
        protected override System.Windows.Size MeasureOverride(System.Windows.Size size) { Measures++; return base.MeasureOverride(size); }
        protected override System.Windows.Size ArrangeOverride(System.Windows.Size size) { Arranges++; return base.ArrangeOverride(size); }
    }
    private static async Task VerifyAnnotation()
    {
        using var module = new CueModule(new CueSettings { FocusHoldEnabled = false, ClickPulseEnabled = false }, () => { });
        var errors = new List<string>(); module.Error += (_, e) => errors.Add(e.Message);
        var backdrop = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(31, 44, 57)) };
        try
        {
            module.ToggleAnnotation();
            var a = module.Annotation ?? throw new Exception(string.Join(";", errors));
            var toolbar = (CueToolbarWindow)a.ToolWindow!;
            backdrop.Left = a.Left; backdrop.Top = a.Top; backdrop.Width = a.Width; backdrop.Height = a.Height; backdrop.Show();
            a.Activate(); await Task.Delay(350);
            if (!module.IsAnnotating || !a.IsVisible || !toolbar.IsVisible || toolbar.Owner != a) throw new Exception("Single-toolbar annotation entry failed.");
            if (((FrameworkElement)toolbar.FindName("MainTools")).Visibility != Visibility.Collapsed) throw new Exception("Main toolbar still visible in annotation mode.");
            a.SelectTool("Arrow"); a.BeginGesture(new Point(100, 180)); a.MoveGesture(new Point(340, 200)); a.EndGesture();
            if (a.Document.Items.Count != 1 || a.Document.Items[0].Tool != "Arrow") throw new Exception("Arrow is not one object.");
            a.SelectTool("Pen"); a.BeginGesture(new Point(100, 260)); a.MoveGesture(new Point(210, 300)); a.MoveGesture(new Point(330, 260)); a.EndGesture();
            a.Undo();
            if (a.Document.Items.Count != 1 || a.Document.Items[0].Tool != "Arrow") throw new Exception("Mixed-type undo is not chronological.");
            a.Redo();
            a.SelectTool("Erase"); a.BeginGesture(new Point(220, 190));
            if (a.Document.Items.Count != 1 || a.Document.Items[0].Tool != "Pen") throw new Exception("Eraser did not remove whole arrow.");
            a.Undo();
            foreach (var shape in new[] { "Rectangle", "Ellipse", "Line", "Highlighter" })
            {
                a.SelectTool(shape); a.BeginGesture(new Point(420, 150 + a.Document.Items.Count * 45));
                a.MoveGesture(new Point(620, 180 + a.Document.Items.Count * 45)); a.EndGesture();
            }
            a.SelectTool("Step"); a.BeginGesture(new Point(75, 180)); a.BeginGesture(new Point(75, 280));
            if (a.Document.NextNumber != 3) throw new Exception("Numbering is inconsistent.");
            a.SelectTool("Text"); a.BeginGesture(new Point(100, 110));
            var editorLayer = (System.Windows.Controls.Canvas)a.FindName("EditorLayer");
            ((System.Windows.Controls.TextBox)editorLayer.Children[0]).Text = "讲清重点，轻松标注";
            a.SelectTool("Pen");
            if (editorLayer.Children.Count != 0 || a.Document.Items.Last().Text != "讲清重点，轻松标注") throw new Exception("Text did not commit into a clean object.");
            a.BeginGesture(new Point(105, 115), 2);
            if (editorLayer.Children.Count != 1) throw new Exception("Double-click text edit failed.");
            ((System.Windows.Controls.TextBox)editorLayer.Children[0]).Text = "cancelled";
            a.Escape();
            if (a.Document.Items.Last().Text == "cancelled" || !module.IsAnnotating) throw new Exception("Esc did not cancel text only.");
            var count = a.Document.Items.Count;
            a.ClearMarks(); if (a.Document.Items.Count != 0) throw new Exception("Clear failed.");
            a.Undo(); if (a.Document.Items.Count != count) throw new Exception("Clear cannot be undone.");
            a.Redo(); a.Undo();
            foreach (var mark in a.Document.Items)
            {
                var hit = mark.Tool is "Text" or "Step" ? mark.Points[0] : mark.Points[^1];
                if (mark.Tool == "Ellipse") hit = new Point(mark.Points[^1].X, (mark.Points[0].Y + mark.Points[^1].Y) / 2);
                if (!CueMarkDrawing.Hit(mark, hit)) throw new Exception($"Missing eraser hit testing for {mark.Tool}.");
            }
            a.SelectTool("Arrow");
            await Task.Delay(150);
            SaveVisual((FrameworkElement)toolbar.Content, "artifacts/cue-annotation-toolbar.png");
            // Exercise a real popup and its command, not a separate mock toolbar.
            ((System.Windows.Controls.Primitives.ButtonBase)toolbar.FindName("ColorTool")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(120);
            var menu = (System.Windows.Controls.ContextMenu)typeof(CueToolbarWindow).GetField("_openMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(toolbar)!;
            if (!menu.IsOpen || menu.Items.Count != 6) throw new Exception("Color popup failed.");
            SaveVisual(menu, "artifacts/cue-annotation-colors.png");
            ((System.Windows.Controls.MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            menu.IsOpen = false;
            if (a.CurrentColor != Color.FromRgb(255, 82, 104)) throw new Exception("Color command failed.");
            toolbar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            await Task.Delay(1850);
            if (toolbar.IsCollapsed) throw new Exception("Annotation auto-collapsed while drawing.");
            toolbar.CollapseToNotch(); await Task.Delay(300); toolbar.Expand(); await Task.Delay(300);
            if (((FrameworkElement)toolbar.FindName("AnnotationTools")).Visibility != Visibility.Visible) throw new Exception("Notch forgot annotation mode.");
            await a.ToggleFreezeAsync();
            if (!a.IsFrozen || !a.IsVisible || !toolbar.IsVisible) throw new Exception("Freeze did not restore annotation surfaces.");
            var frozen = (System.Windows.Media.Imaging.BitmapSource)((System.Windows.Controls.Image)a.FindName("FrozenImage")).Source;
            SaveBitmap(frozen, "artifacts/cue-annotation-frozen.png");
            var pixel = new byte[4];
            frozen.CopyPixels(new Int32Rect((int)(220 * frozen.PixelWidth / a.ActualWidth), (int)(190 * frozen.PixelHeight / a.ActualHeight), 1, 1), pixel, 4, 0);
            if (Math.Abs(pixel[0] - 57) > 4 || Math.Abs(pixel[1] - 44) > 4 || Math.Abs(pixel[2] - 31) > 4) throw new Exception($"Freeze captured an annotation or incorrect backdrop: BGRA {string.Join(',', pixel)}.");
            SaveBitmap(await a.CompositeAsync(), "artifacts/cue-annotation-composite.png");
            a.ToggleDesktop();
            if (a.IsFrozen || !a.IsOperatingDesktop) throw new Exception("Desktop operation did not unfreeze.");
            var style = GetWindowLongPtr(new System.Windows.Interop.WindowInteropHelper(a).Handle, -20).ToInt64();
            if ((style & 0x20) == 0) throw new Exception("Desktop mode does not pass clicks through.");
            a.ToggleDesktop();
            if ((GetWindowLongPtr(new System.Windows.Interop.WindowInteropHelper(a).Handle, -20).ToInt64() & 0x20) != 0) throw new Exception("Drawing did not restore input.");
            a.BeginGesture(new Point(700, 200)); a.MoveGesture(new Point(800, 200)); a.Escape();
            if (!module.IsAnnotating || a.Document.Items.Count != count) throw new Exception("Esc did not cancel draft.");
            a.Escape(); await Task.Delay(300);
            if (module.IsAnnotating || a.IsVisible || !toolbar.IsVisible) throw new Exception("Exit did not return to Cue.");
            module.ToggleAnnotation(); await Task.Delay(300);
            if (!ReferenceEquals(a, module.Annotation) || a.Document.Items.Count != count) throw new Exception("Re-entry lost the session.");
            module.ExitAnnotation();
            module.Stop(); module.ToggleAnnotation(); await Task.Delay(250);
            if (ReferenceEquals(a, module.Annotation) || module.Annotation?.Document.Items.Count != count) throw new Exception("Module restart lost document or reused closed window.");
            module.ExitAnnotation();
            if (errors.Count > 0) throw new Exception(string.Join(";", errors));
            Console.WriteLine("Annotation PASS: single toolbar; all drawing types; chronological undo/redo; whole-object eraser; undoable clear; text commit/edit/cancel; popup color; persistent session; no auto-collapse; notch mode; clean freeze and PNG composition; desktop click-through; two-stage Esc.");
        }
        finally { module.Dispose(); backdrop.Close(); }
        static void SaveVisual(FrameworkElement visual, string path)
        {
            visual.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * 2), (int)Math.Ceiling(visual.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(visual); SaveBitmap(bitmap, path);
        }
        static void SaveBitmap(System.Windows.Media.Imaging.BitmapSource bitmap, string path)
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(System.IO.Path.GetFullPath(path)); encoder.Save(stream);
        }
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    private static async Task VerifyLaser()
    {
        foreach (var hz in new[] { 30, 60, 144, 240 })
        {
            var stroke = new CueLaserStroke();
            for (var frame = 0; frame <= hz; frame++) stroke.Update(new Point(20 + frame * 600d / hz, 40), (double)frame / hz);
            if (stroke.SampleCount > 128) throw new Exception("Unbounded laser samples.");
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) stroke.Draw(context, 1);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(660, 80, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[660 * 80 * 4]; bitmap.CopyPixels(pixels, 660 * 4, 0);
            for (var x = 465; x < 620; x++)
                if (pixels[(40 * 660 + x) * 4 + 3] == 0) throw new Exception($"Laser gap at {hz} Hz / x={x}.");
            stroke.Update(new Point(620, 40), 1.36);
            if (stroke.SampleCount != 0) throw new Exception("Stationary tail did not expire.");
            stroke.Update(new Point(620, 40), 1.4);
            stroke.Update(new Point(1300, 40), 1.41);
            if (stroke.SampleCount != 1) throw new Exception("Cursor teleport connected a beam.");
            stroke.Clear();
            if (stroke.SampleCount != 0) throw new Exception("Laser disable retained trail.");
        }
        var preview = new DrawingVisual();
        using (var dc = preview.RenderOpen())
        {
            for (var panel = 0; panel < 2; panel++)
            {
                dc.PushTransform(new TranslateTransform(panel * 460, 0));
                dc.DrawRectangle(panel == 0 ? new SolidColorBrush(Color.FromRgb(249, 247, 243)) : new SolidColorBrush(Color.FromRgb(35, 33, 43)), null, new Rect(0, 0, 460, 310));
                for (var row = 0; row < 3; row++)
                {
                    var label = new[] { "Moving · continuous / tapered", "Stopped · quiet light point", "Turning · connected stroke" }[row];
                    dc.DrawText(new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 12, panel == 0 ? Brushes.DimGray : Brushes.LightGray, 1), new Point(24, 16 + row * 100));
                    var stroke = new CueLaserStroke();
                    for (var frame = 0; frame <= 36; frame++)
                    {
                        var t = frame / 36d;
                        stroke.Update(row == 2 ? new Point(255 + 65 * Math.Cos(t * 5), 265 + 22 * Math.Sin(t * 5))
                            : new Point(70 + 300 * t, 66 + row * 100 + 14 * Math.Sin(t * 6)), frame / 120d);
                    }
                    if (row == 1) stroke.Update(new Point(370, 166 + 14 * Math.Sin(6)), .66);
                    stroke.Draw(dc, row == 1 ? .66 : .3);
                }
                dc.Pop();
            }
        }
        var output = new System.Windows.Media.Imaging.RenderTargetBitmap(1840, 620, 192, 192, PixelFormats.Pbgra32);
        output.Render(preview);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(output));
        using (var stream = System.IO.File.Create(System.IO.Path.GetFullPath("artifacts/cue-laser-preview.png"))) encoder.Save(stream);
        var overlay = new CueOverlayWindow(new CueSettings { LaserEnabled = true, ClickPulseEnabled = false });
        try
        {
            overlay.Show(); await Task.Delay(250);
            var content = overlay.Content;
            var rendering = content.GetType().GetField("_rendering", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            if (!(bool)rendering.GetValue(content)!) throw new Exception("Overlay render subscription missing.");
            overlay.Hide();
            if ((bool)rendering.GetValue(content)!) throw new Exception("Hidden overlay kept rendering.");
            overlay.Show(); await Task.Delay(100);
            overlay.StopRendering();
            if ((bool)rendering.GetValue(content)!) throw new Exception("Stopped overlay kept rendering.");
        }
        finally { overlay.Close(); }
        Console.WriteLine("Laser PASS: unbroken strokes at 30/60/144/240 Hz input samples, tail expiration, teleport reset, bounded samples, hide/resume/stop rendering; light/dark preview rendered.");
    }
    private static async Task VerifyToolbar()
    {
        using var module = new CueModule(new CueSettings { PointerRingEnabled = true, MagnifierEnabled = true }, () => { });
        var toolbar = new CueToolbarWindow(module);
        try
        {
            toolbar.CollapseToNotch();
            await Task.Delay(200);
            CheckNotch();
            SaveVisual("collapsed");
            // Exercise the actual routed button handlers, including focus left by a click.
            var notch = (System.Windows.Controls.Button)toolbar.FindName("Notch");
            notch.Focus();
            notch.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(500);
            if (toolbar.IsCollapsed || !toolbar.IsVisible || toolbar.ActualWidth < 500) throw new Exception("Toolbar did not expand.");
            SaveVisual("expanded");
            ((System.Windows.Controls.Button)toolbar.FindName("CollapseButton")).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(500);
            CheckNotch();
            toolbar.Expand();
            toolbar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
                { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            await Task.Delay(2200);
            if (toolbar.IsMouseOver) throw new Exception("Move cursor away from toolbar before auto-collapse probe.");
            CheckNotch();
            toolbar.Expand(); await Task.Delay(35);
            if (((FrameworkElement)toolbar.FindName("ExpandedSurface")).Opacity > .01) throw new Exception("Icons appeared before shell expansion.");
            toolbar.CollapseToNotch(); await Task.Delay(30); toolbar.Expand(); await Task.Delay(30);
            toolbar.CollapseToNotch(); await Task.Delay(500); CheckNotch();
            toolbar.Expand(); await Task.Delay(35);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(toolbar).Handle;
            GetWindowRect(hwnd, out var firstBounds);
            if (WindowFromPoint(new ProbePoint(firstBounds.Left + 2, firstBounds.Top + 40)) == hwnd)
                throw new Exception("Invisible area of expanded animation host intercepted desktop input.");
            var mask = (RectangleGeometry)((FrameworkElement)toolbar.Content).Clip;
            var firstClipWidth = mask.Rect.Width;
            await Task.Delay(100); GetWindowRect(hwnd, out var secondBounds);
            if (!firstBounds.Equals(secondBounds) || mask.Rect.Width <= firstClipWidth) throw new Exception("Reveal resized HWND or failed to animate clip.");
            var rectBeforeReverse = mask.Rect;
            toolbar.CollapseToNotch();
            if (Math.Abs(mask.Rect.Width - rectBeforeReverse.Width) > 1) throw new Exception("Reverse jumped instead of retaining position.");
            await Task.Delay(500); CheckNotch();
            var motion = new CueRevealAnimation { FromRect = new Rect(0, -8, 72, 20), ToRect = new Rect(-338, 8, 748, 72) };
            var sample = motion.Sample(.095);
            var reverse = new CueRevealAnimation { FromRect = sample.Rect, ToRect = motion.FromRect, InitialVelocity = sample.Velocity };
            var start = reverse.Sample(0);
            if (start.Rect != sample.Rect || Math.Abs(start.Velocity.Width - sample.Velocity.Width) > .0001) throw new Exception("Reveal velocity is discontinuous at reversal.");
            toolbar.Expand(); await Task.Delay(40); toolbar.Hide(); toolbar.Show();
            toolbar.CollapseToNotch(); await Task.Delay(40); toolbar.Hide(); toolbar.Show();
            await Task.Delay(50); CheckNotch();
            Console.WriteLine("Toolbar PASS: persistent top notch, click expansion, manual collapse, auto-collapse after pointer leave; rendered both states.");
        }
        finally { toolbar.Close(); }

        void CheckNotch()
        {
            if (!toolbar.IsVisible || !toolbar.IsCollapsed) throw new Exception("Collapsed toolbar disappeared.");
            var hwnd = new System.Windows.Interop.WindowInteropHelper(toolbar).Handle;
            if (!GetWindowRect(hwnd, out var rect)) throw new Exception("Cannot query toolbar bounds.");
            var dpi = VisualTreeHelper.GetDpi(toolbar);
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            if (rect.Top != screen.WorkingArea.Top || Math.Abs(rect.Right - rect.Left - 72 * dpi.DpiScaleX) > 1
                || Math.Abs(rect.Bottom - rect.Top - 12 * dpi.DpiScaleY) > 1)
                throw new Exception("Notch native bounds are not compact and top-anchored.");
        }
        void SaveVisual(string state)
        {
            toolbar.UpdateLayout();
            var visual = (FrameworkElement)toolbar.Content;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth * 2,
                (int)visual.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(System.IO.Path.GetFullPath($"artifacts/cue-toolbar-{state}.png"));
            encoder.Save(stream);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct ProbePoint(int X, int Y);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(ProbePoint point);
    private static async Task MeasureLifecycle()
    {
        var errors = new List<string>();
        var settings = new CueSettings { Enabled = true, FocusHoldEnabled = false, MagnifierEnabled = true, ClickPulseEnabled = false };
        using var module = new CueModule(settings, () => { });
        module.Error += (_, error) => errors.Add(error.Message);
        if (!module.Start()) throw new Exception("Module start failed.");
        var toolbarField = typeof(CueModule).GetField("_toolbar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        if (toolbarField.GetValue(module) is not CueToolbarWindow toolbar || !toolbar.IsVisible || !toolbar.IsCollapsed)
            throw new Exception("Module start did not leave an accessible notch.");
        await Task.Delay(1200);
        var runtime = typeof(CueModule).GetField("_magnifier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        if (runtime.GetValue(module) is not CueGpuMagnifier first) throw new Exception("GPU runtime missing.");
        module.SetPaused(true);
        if (runtime.GetValue(module) is not null) throw new Exception("Pause retained GPU runtime.");
        module.SetPaused(false);
        await Task.Delay(1200);
        if (runtime.GetValue(module) is not CueGpuMagnifier second || ReferenceEquals(first, second)) throw new Exception("Resume failed.");
        module.ResetAll();
        if (runtime.GetValue(module) is not null || settings.MagnifierEnabled) throw new Exception("Reset retained lens.");
        module.Stop();
        if (toolbar.IsVisible || toolbarField.GetValue(module) is not null) throw new Exception("Module stop retained toolbar.");
        if (module.IsRunning || errors.Count != 0) throw new Exception(string.Join("; ", errors));
        Console.WriteLine(JsonSerializer.Serialize(new { Lifecycle = "PASS: start, pause disposes GPU, resume recreates GPU, reset, stop", Errors = errors }));
    }
    private static async Task MeasureLegacy()
    {
        var intervals = new List<double>();
        var work = new List<double>();
        var clock = Stopwatch.StartNew();
        var previous = 0d;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var start = clock.Elapsed.TotalMilliseconds;
            if (previous > 0) intervals.Add(start - previous);
            previous = start;
            var p = System.Windows.Forms.Cursor.Position;
            using var bitmap = new System.Drawing.Bitmap(130, 130, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
                g.CopyFromScreen(p.X - 65, p.Y - 65, 0, 0, new System.Drawing.Size(130, 130));
            var handle = bitmap.GetHbitmap();
            try
            {
                var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
            }
            finally { DeleteObject(handle); }
            work.Add(clock.Elapsed.TotalMilliseconds - start);
        };
        timer.Start();
        try { await Task.Delay(6000); }
        finally { timer.Stop(); }
        intervals.Sort(); work.Sort();
        Console.WriteLine(JsonSerializer.Serialize(new { Backend = "Legacy UI-thread GDI capture (without final drawing)",
            Frames = work.Count, Seconds = clock.Elapsed.TotalSeconds,
            MedianInterval = intervals[intervals.Count / 2], P95Interval = intervals[(int)(intervals.Count * .95)],
            MedianWork = work[work.Count / 2], P95Work = work[(int)(work.Count * .95)] }));
    }
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    private sealed class MotionPattern : FrameworkElement
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public MotionPattern()
        {
            Loaded += (_, _) => CompositionTarget.Rendering += Render;
            Unloaded += (_, _) => CompositionTarget.Rendering -= Render;
        }
        private void Render(object? sender, EventArgs e) => InvalidateVisual();
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.MidnightBlue, null, new Rect(RenderSize));
            var x = (_clock.Elapsed.TotalMilliseconds * .2) % Math.Max(1, ActualWidth);
            dc.DrawRectangle(Brushes.Orange, null, new Rect(x, 0, 16, ActualHeight));
            for (var y = 40; y < ActualHeight; y += 40)
                dc.DrawLine(new System.Windows.Media.Pen(Brushes.White, 1), new Point(0, y), new Point(ActualWidth, y));
        }
    }
}

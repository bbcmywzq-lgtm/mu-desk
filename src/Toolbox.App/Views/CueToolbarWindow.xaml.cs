using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using PersonalToolbox.Modules;
using PersonalToolbox.Native;
using PersonalToolbox.Services;
using Forms = System.Windows.Forms;

namespace PersonalToolbox.Views;

public partial class CueToolbarWindow : Window
{
    private readonly CueModule _module;
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private string _screenName;
    private bool _ready;
    private bool _bottomDock;
    private int _transitionVersion;
    private CueRevealAnimation? _revealMotion;
    private AnimationClock? _revealClock;
    private bool _transitioning;
    public bool IsCollapsed { get; private set; } = true;

    public CueToolbarWindow(CueModule module)
    {
        _module = module;
        _screenName = Forms.Screen.FromPoint(Forms.Cursor.Position).DeviceName;
        InitializeComponent();
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!_module.IsAnnotating && _openMenu?.IsOpen != true && !IsMouseOver && !IsKeyboardFocusWithin) CollapseToNotch();
        };
        SourceInitialized += (_, _) =>
        {
            _ready = true;
            CaptureInterop.ExcludeWindowFromCapture(new WindowInteropHelper(this).Handle);
            Reposition();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition);
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        Closed += (_, _) =>
        {
            _collapseTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _collapseTimer.Stop();
                if (_transitioning) FinishReveal(IsCollapsed);
            }
        };
    }

    public void Expand()
    {
        _collapseTimer.Stop();
        if (IsKeyboardFocusWithin) Keyboard.ClearFocus();
        Refresh();
        Transition(false);
        // Opening from Desk leaves the cursor elsewhere; preserve a short grace
        // period so there is time to move to the toolbar.
        _collapseTimer.Interval = TimeSpan.FromSeconds(4);
        if (!_module.IsAnnotating) _collapseTimer.Start();
    }

    public void CollapseToNotch()
    {
        _collapseTimer.Stop();
        if (_openMenu is not null) _openMenu.IsOpen = false;
        if (IsKeyboardFocusWithin) Keyboard.ClearFocus();
        Transition(true);
    }

    private void Transition(bool collapsed)
    {
        if (collapsed && IsCollapsed && !_transitioning && IsVisible) return;
        var animate = IsVisible && SystemParameters.ClientAreaAnimation;
        var oldWidth = Width;
        var current = RevealClip.Rect;
        var velocity = _revealMotion?.Sample(_revealClock?.CurrentTime?.TotalSeconds ?? 0).Velocity ?? default;
        RevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        var version = ++_transitionVersion;
        IsCollapsed = collapsed;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _screenName) ?? Forms.Screen.PrimaryScreen!;
        var fullWidth = Math.Min(_module.IsAnnotating ? 820 : 748,
            Math.Max(240, screen.WorkingArea.Width / VisualTreeHelper.GetDpi(this).DpiScaleX - 16));
        var hostWidth = Math.Max(fullWidth, oldWidth);
        // Lay controls out at their final size ONCE. The HWND stays fixed for
        // the whole animation; only the retained clip and opacities change.
        ExpandedSurface.Width = fullWidth;
        current.X += (hostWidth - oldWidth) / 2;
        RevealClip.Rect = current;
        Width = hostWidth; Height = 80;
        Notch.Visibility = Visibility.Visible; ExpandedSurface.Visibility = Visibility.Visible;
        Notch.IsHitTestVisible = collapsed; ExpandedSurface.IsHitTestVisible = !collapsed;
        var contentOpacity = ExpandedSurface.Opacity; var notchOpacity = Notch.Opacity;
        Notch.BeginAnimation(OpacityProperty, null); ExpandedSurface.BeginAnimation(OpacityProperty, null);
        Notch.Opacity = notchOpacity; ExpandedSurface.Opacity = contentOpacity;
        if (!IsVisible) Show();
        Reposition();
        if (!animate) { FinishReveal(collapsed); return; }
        _transitioning = true;
        ExpandedSurface.CacheMode = new BitmapCache();
        _revealMotion = new CueRevealAnimation { FromRect = current,
            ToRect = collapsed ? new Rect((hostWidth - 72) / 2, -8, 72, 20) : new Rect((hostWidth - fullWidth) / 2, 8, fullWidth, 72),
            InitialVelocity = velocity, Duration = TimeSpan.FromSeconds(CueRevealAnimation.SettleSeconds) };
        Timeline.SetDesiredFrameRate(_revealMotion, 120);
        _revealClock = _revealMotion.CreateClock();
        _revealClock.Completed += (_, _) => { if (version == _transitionVersion) FinishReveal(collapsed); };
        RevealClip.ApplyAnimationClock(RectangleGeometry.RectProperty, _revealClock);
        if (_bottomDock) Fade(Root, Root.Opacity, collapsed ? 0 : 1, 180, collapsed ? 80 : 0);
        Fade(ExpandedSurface, contentOpacity, collapsed ? 0 : 1, collapsed ? 90 : 150, !collapsed && contentOpacity < .01 ? 65 : 0);
        Fade(Notch, notchOpacity, collapsed ? 1 : 0, 100, collapsed ? 130 : 0);
        RevealClip.BeginAnimation(RectangleGeometry.RadiusXProperty, new DoubleAnimation(RevealClip.RadiusX, collapsed ? 8 : 16, TimeSpan.FromMilliseconds(220)));
        RevealClip.BeginAnimation(RectangleGeometry.RadiusYProperty, new DoubleAnimation(RevealClip.RadiusY, collapsed ? 8 : 16, TimeSpan.FromMilliseconds(220)));
    }

    private static void Fade(UIElement element, double from, double to, int milliseconds, int delay)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { BeginTime = TimeSpan.FromMilliseconds(delay) };
        Timeline.SetDesiredFrameRate(animation, 120);
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void FinishReveal(bool collapsed)
    {
        ++_transitionVersion; _transitioning = false; _revealMotion = null; _revealClock = null;
        ExpandedSurface.CacheMode = null;
        Root.BeginAnimation(OpacityProperty, null); Root.Opacity = 1;
        RevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        RevealClip.BeginAnimation(RectangleGeometry.RadiusXProperty, null); RevealClip.BeginAnimation(RectangleGeometry.RadiusYProperty, null);
        Notch.BeginAnimation(OpacityProperty, null); ExpandedSurface.BeginAnimation(OpacityProperty, null);
        Notch.Opacity = collapsed ? 1 : 0; ExpandedSurface.Opacity = collapsed ? 0 : 1;
        Notch.Visibility = collapsed ? Visibility.Visible : Visibility.Hidden;
        ExpandedSurface.Visibility = collapsed ? Visibility.Hidden : Visibility.Visible;
        if (collapsed) _bottomDock = false;
        Width = collapsed ? 72 : ExpandedSurface.Width; Height = collapsed ? 12 : 80;
        RevealClip.Rect = collapsed ? new Rect(0, -8, 72, 20) : new Rect(0, 8, Width, 72);
        RevealClip.RadiusX = RevealClip.RadiusY = collapsed ? 8 : 16;
        Reposition();
    }

    private void Reposition()
    {
        if (!_ready) return;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _screenName) ?? Forms.Screen.PrimaryScreen!;
        _screenName = screen.DeviceName;
        var bounds = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var pixelsWide = (int)Math.Round(Width * dpi.DpiScaleX);
        var pixelsHigh = (int)Math.Round(Height * dpi.DpiScaleY);
        // Resize the actual HWND too: the collapsed state must not leave a
        // transparent full-width window intercepting desktop clicks.
        SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1),
            bounds.Left + (bounds.Width - pixelsWide) / 2,
            _bottomDock ? bounds.Bottom - pixelsHigh - (int)(12 * dpi.DpiScaleY) : bounds.Top,
            pixelsWide, pixelsHigh, 0x10);
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(Reposition);
    }

    public void Refresh()
    {
        RingButton.IsChecked = _module.Settings.PointerRingEnabled;
        LaserButton.IsChecked = _module.Settings.LaserEnabled;
        ClickButton.IsChecked = _module.Settings.ClickPulseEnabled;
        SpotlightButton.IsChecked = _module.Settings.SpotlightEnabled;
        MagnifierButton.IsChecked = _module.Settings.MagnifierEnabled;
        MainTools.Visibility = _module.IsAnnotating ? Visibility.Collapsed : Visibility.Visible;
        AnnotationTools.Visibility = _module.IsAnnotating ? Visibility.Visible : Visibility.Collapsed;
        RefreshAnnotation();
    }

    private void Window_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => _collapseTimer.Stop();
    private void Window_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (IsCollapsed || _module.IsAnnotating || _openMenu?.IsOpen == true) return;
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(1600);
        _collapseTimer.Start();
    }
    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_module.IsAnnotating && e.Key == Key.Escape) { _module.Annotation?.Escape(); e.Handled = true; }
        else if (e.Key == Key.Escape && !IsCollapsed) { CollapseToNotch(); e.Handled = true; }
        else if (_module.IsAnnotating && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.Z) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _module.Annotation?.Redo(); else _module.Annotation?.Undo(); e.Handled = true; }
            if (e.Key == Key.Y) { _module.Annotation?.Redo(); e.Handled = true; }
        }
    }
    private void Drag_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _collapseTimer.Stop();
        DragMove();
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        if (_module.IsAnnotating && _module.Annotation is { } annotation)
            screen = Forms.Screen.FromHandle(new WindowInteropHelper(annotation).Handle);
        _screenName = screen.DeviceName;
        _bottomDock = cursor.Y > screen.WorkingArea.Top + screen.WorkingArea.Height / 2;
        Reposition(); e.Handled = true;
    }
    private void Expand_OnClick(object sender, RoutedEventArgs e) => Expand();
    private void Ring_OnClick(object sender, RoutedEventArgs e) => _module.TogglePointerRing();
    private void Laser_OnClick(object sender, RoutedEventArgs e) => _module.ToggleLaser();
    private void Click_OnClick(object sender, RoutedEventArgs e) => _module.ToggleClickPulse();
    private void Spotlight_OnClick(object sender, RoutedEventArgs e) => _module.ToggleSpotlight();
    private void Magnifier_OnClick(object sender, RoutedEventArgs e) => _module.ToggleMagnifier();
    private void Annotation_OnClick(object sender, RoutedEventArgs e) => _module.ToggleAnnotation();
    private void Screenshot_OnClick(object sender, RoutedEventArgs e) => _module.CaptureCurrentMonitor();
    private void RegionScreenshot_OnClick(object sender, RoutedEventArgs e) => _module.CaptureRegion();
    private void Reset_OnClick(object sender, RoutedEventArgs e) => _module.ResetAll();
    private void Settings_OnClick(object sender, RoutedEventArgs e) => _module.OpenSettings();
    private void Hide_OnClick(object sender, RoutedEventArgs e) => CollapseToNotch();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}

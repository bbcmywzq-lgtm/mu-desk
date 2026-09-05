using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using PersonalToolbox.Services;
using Forms = System.Windows.Forms;

namespace PersonalToolbox.Views;

public partial class FileShelfWindow
{
    private const double HandleWidth = 22, HandleHeight = 76;
    private readonly DispatcherTimer _collapseTimer = new();
    private Rect _hostBounds, _fullBounds, _handleBounds;
    private CueRevealAnimation? _motion;
    private AnimationClock? _clock;
    private int _motionVersion;
    private bool _transitioning, _incomingDrag, _outgoingDrag, _confirming;
    internal bool IsTransitioning => _transitioning;
    internal Rect VisibleReveal => RevealClip.Rect;
    internal int HostPlacementCount { get; private set; }
    internal bool? AnimationEnabledForDiagnostics { get; set; }
    public event EventHandler? ExpansionChanged;

    private void InitializeMotion()
    {
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (IsExpanded && !IsMouseOver && !_incomingDrag && !_outgoingDrag && !_confirming) ShowCollapsed();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(RefreshPlacement);
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { _collapseTimer.Stop(); if (_transitioning) FinishTransition(); }
        };
        Closed += (_, _) =>
        {
            _collapseTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            CancelMotion();
        };
    }

    private void Window_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => _collapseTimer.Stop();
    private void Window_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ScheduleCollapse();
    private void ScheduleCollapse(int milliseconds = 1800)
    {
        _collapseTimer.Stop();
        if (!IsExpanded || _outgoingDrag || _confirming || !IsVisible) return;
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _collapseTimer.Start();
    }

    private void Transition(bool expanded)
    {
        if (IsExpanded == expanded && IsVisible) return;
        var animate = IsVisible && (AnimationEnabledForDiagnostics ?? SystemParameters.ClientAreaAnimation);
        var previousHost = _hostBounds;
        var rect = RevealClip.Rect;
        var velocity = _motion?.Sample(_clock?.CurrentTime?.TotalSeconds ?? 0).Velocity ?? default;
        var panelOpacity = ExpandedPanel.Opacity;
        var handleOpacity = CollapsedButton.Opacity;
        var radius = RevealClip.RadiusX;
        CancelMotion();
        RevealClip.RadiusX = RevealClip.RadiusY = radius;
        IsExpanded = expanded;
        CalculatePlacement();
        ExpandedPanel.Width = _fullBounds.Width;
        ExpandedPanel.Height = _fullBounds.Height;
        ApplyHostBounds(_fullBounds);
        rect.Offset(previousHost.X - _fullBounds.X, previousHost.Y - _fullBounds.Y);
        Canvas.SetLeft(CollapsedButton, _handleBounds.X - _fullBounds.X);
        Canvas.SetTop(CollapsedButton, _handleBounds.Y - _fullBounds.Y);
        ExpandedPanel.Visibility = CollapsedButton.Visibility = Visibility.Visible;
        ExpandedPanel.IsHitTestVisible = expanded;
        CollapsedButton.IsHitTestVisible = !expanded;
        var target = expanded ? new Rect(0, 0, Width, Height)
            : new Rect(_handleBounds.X - _fullBounds.X, _handleBounds.Y - _fullBounds.Y, HandleWidth, HandleHeight);
        RevealClip.Rect = animate ? rect : target;
        if (!IsVisible) Show();
        ExpansionChanged?.Invoke(this, EventArgs.Empty);
        if (!animate) { FinishTransition(); return; }
        _transitioning = true;
        ExpandedPanel.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(this).DpiScaleX);
        _motion = new CueRevealAnimation { FromRect = rect, ToRect = target, InitialVelocity = velocity,
            Duration = TimeSpan.FromSeconds(CueRevealAnimation.SettleSeconds) };
        Timeline.SetDesiredFrameRate(_motion, 120);
        _clock = _motion.CreateClock();
        var version = _motionVersion;
        _clock.Completed += (_, _) => { if (version == _motionVersion) FinishTransition(); };
        RevealClip.ApplyAnimationClock(RectangleGeometry.RectProperty, _clock);
        FadeShelf(ExpandedPanel, panelOpacity, expanded ? 1 : 0, expanded ? 180 : 110);
        FadeShelf(CollapsedButton, handleOpacity, expanded ? 0 : 1, 150);
        RevealClip.BeginAnimation(RectangleGeometry.RadiusXProperty,
            new DoubleAnimation(RevealClip.RadiusX, expanded ? 18 : 10, TimeSpan.FromMilliseconds(180)));
        RevealClip.BeginAnimation(RectangleGeometry.RadiusYProperty,
            new DoubleAnimation(RevealClip.RadiusY, expanded ? 18 : 10, TimeSpan.FromMilliseconds(180)));
    }

    private void CancelMotion()
    {
        ++_motionVersion; _transitioning = false; _motion = null; _clock = null;
        RevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        RevealClip.BeginAnimation(RectangleGeometry.RadiusXProperty, null);
        RevealClip.BeginAnimation(RectangleGeometry.RadiusYProperty, null);
        ExpandedPanel.BeginAnimation(OpacityProperty, null);
        CollapsedButton.BeginAnimation(OpacityProperty, null);
        ExpandedPanel.CacheMode = null;
    }

    private void FinishTransition()
    {
        CancelMotion();
        ApplyHostBounds(IsExpanded ? _fullBounds : _handleBounds);
        RevealClip.Rect = new Rect(0, 0, Width, Height);
        RevealClip.RadiusX = RevealClip.RadiusY = IsExpanded ? 18 : 10;
        ExpandedPanel.Opacity = IsExpanded ? 1 : 0;
        CollapsedButton.Opacity = IsExpanded ? 0 : 1;
        ExpandedPanel.Visibility = IsExpanded ? Visibility.Visible : Visibility.Hidden;
        CollapsedButton.Visibility = IsExpanded ? Visibility.Hidden : Visibility.Visible;
        Canvas.SetLeft(CollapsedButton, IsExpanded ? _handleBounds.X - _fullBounds.X : 0);
        Canvas.SetTop(CollapsedButton, IsExpanded ? _handleBounds.Y - _fullBounds.Y : 0);
    }

    private static void FadeShelf(UIElement element, double from, double to, int milliseconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds));
        Timeline.SetDesiredFrameRate(animation, 120);
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void CalculatePlacement()
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.DisplayDeviceName)
            ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.First();
        var dpi = VisualTreeHelper.GetDpi(this);
        var work = new Rect(screen.WorkingArea.Left / dpi.DpiScaleX, screen.WorkingArea.Top / dpi.DpiScaleY,
            screen.WorkingArea.Width / dpi.DpiScaleX, screen.WorkingArea.Height / dpi.DpiScaleY);
        var w = Math.Min(360, work.Width);
        var h = Math.Min(Math.Clamp(work.Height * .65, 380, 560), Math.Max(100, work.Height - 16));
        var left = string.Equals(_settings.DockSide, "Left", StringComparison.OrdinalIgnoreCase);
        var center = work.Top + work.Height * _settings.VerticalPosition;
        _fullBounds = new Rect(left ? work.Left : work.Right - w,
            Math.Clamp(center - h / 2, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - h - 8)), w, h);
        _handleBounds = new Rect(left ? work.Left : work.Right - HandleWidth,
            Math.Clamp(center - HandleHeight / 2, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - HandleHeight - 8)), HandleWidth, HandleHeight);
        CollapseArrow.Data = Geometry.Parse(left ? "M 5,0 L 0,5 5,10" : "M 0,0 L 5,5 0,10");
        if (_settings.DisplayDeviceName != screen.DeviceName)
            _module.UpdateDockPlacement(_settings.DockSide, screen.DeviceName, _settings.VerticalPosition);
    }

    private void ApplyHostBounds(Rect bounds)
    {
        _hostBounds = bounds;
        Width = bounds.Width; Height = bounds.Height;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) { Left = bounds.Left; Top = bounds.Top; return; }
        var dpi = VisualTreeHelper.GetDpi(this);
        SetWindowPos(hwnd, new nint(-1), (int)Math.Round(bounds.X * dpi.DpiScaleX), (int)Math.Round(bounds.Y * dpi.DpiScaleY),
            (int)Math.Round(bounds.Width * dpi.DpiScaleX), (int)Math.Round(bounds.Height * dpi.DpiScaleY), 0x10);
        HostPlacementCount++;
    }

    private void DisplayChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(RefreshPlacement);
    }
    private void RefreshPlacement()
    {
        if (_closingForModuleStop) return;
        CalculatePlacement();
        ExpandedPanel.Width = _fullBounds.Width; ExpandedPanel.Height = _fullBounds.Height;
        FinishTransition();
    }
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}

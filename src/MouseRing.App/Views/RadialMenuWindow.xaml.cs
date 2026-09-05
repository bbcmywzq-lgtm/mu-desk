using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using MouseRing.Core;
using MouseRing.Interop;
using Point = System.Windows.Point;

namespace MouseRing.Views;

public partial class RadialMenuWindow : Window
{
    private const double CancelDiameter = 52;
    private const double EdgeMargin = 8;
    private const double WindowPadding = 30;

    private NativeMethods.POINT _centerScreen;
    private double _dpiScale = 1;

    public RadialMenuWindow()
    {
        InitializeComponent();
    }

    public Direction SelectedDirection => MenuControl.SelectedDirection;

    internal void ShowAt(
        NativeMethods.POINT anchor,
        double menuDiameter,
        IReadOnlyDictionary<Direction, ActionKind> actions)
    {
        var monitor = NativeMethods.MonitorFromPoint(anchor, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            Size = Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        _dpiScale = GetMonitorScale(monitor);
        var radiusPixels = (int)Math.Round((menuDiameter / 2) * _dpiScale);
        var edgeMarginPixels = (int)Math.Round(EdgeMargin * _dpiScale);
        var paddingPixels = (int)Math.Round(WindowPadding * _dpiScale);

        var centerX = Math.Clamp(
            anchor.X,
            monitorInfo.Work.Left + radiusPixels + edgeMarginPixels,
            monitorInfo.Work.Right - radiusPixels - edgeMarginPixels);
        var centerY = Math.Clamp(
            anchor.Y,
            monitorInfo.Work.Top + radiusPixels + edgeMarginPixels,
            monitorInfo.Work.Bottom - radiusPixels - edgeMarginPixels);
        _centerScreen = new NativeMethods.POINT(centerX, centerY);

        var left = Math.Min(anchor.X, centerX - radiusPixels) - paddingPixels;
        var top = Math.Min(anchor.Y, centerY - radiusPixels) - paddingPixels;
        var right = Math.Max(anchor.X, centerX + radiusPixels) + paddingPixels;
        var bottom = Math.Max(anchor.Y, centerY + radiusPixels) + paddingPixels;
        var widthPixels = Math.Max(1, right - left);
        var heightPixels = Math.Max(1, bottom - top);

        Width = widthPixels / _dpiScale;
        Height = heightPixels / _dpiScale;
        MenuControl.Center = new Point((centerX - left) / _dpiScale, (centerY - top) / _dpiScale);
        MenuControl.Anchor = new Point((anchor.X - left) / _dpiScale, (anchor.Y - top) / _dpiScale);
        MenuControl.OuterRadius = menuDiameter / 2;
        MenuControl.InnerRadius = CancelDiameter / 2;
        MenuControl.SelectedDirection = Direction.None;
        MenuControl.SetItems(actions);

        Opacity = 0;
        if (!IsVisible)
        {
            Show();
        }

        var handle = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            left,
            top,
            widthPixels,
            heightPixels,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80))
            {
                FillBehavior = FillBehavior.Stop,
            });
        Opacity = 1;
    }

    internal void UpdatePointer(NativeMethods.POINT screenPoint)
    {
        var direction = DirectionResolver.Resolve(
            screenPoint.X - _centerScreen.X,
            screenPoint.Y - _centerScreen.Y,
            (CancelDiameter / 2) * _dpiScale);
        if (direction == MenuControl.SelectedDirection)
        {
            return;
        }

        MenuControl.SelectedDirection = direction;
        MenuControl.InvalidateVisual();
    }

    internal void HideMenu()
    {
        BeginAnimation(OpacityProperty, null);
        MenuControl.SelectedDirection = Direction.None;
        Hide();
    }

    internal void EnableInteractivePreview()
    {
        ShowInTaskbar = true;
        ShowActivated = true;
        Focusable = true;
    }

    private static double GetMonitorScale(nint monitor)
    {
        try
        {
            return NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0
                ? Math.Max(1, dpiX / 96d)
                : 1;
        }
        catch (DllNotFoundException)
        {
            return 1;
        }
        catch (EntryPointNotFoundException)
        {
            return 1;
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MouseRing.Core;
using MouseRing.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace MouseRing.Views;

public sealed class RadialMenuControl : FrameworkElement
{
    private static readonly Brush SurfaceBrush = FrozenBrush(242, 27, 29, 36);
    private static readonly Brush SelectedSurfaceBrush = FrozenBrush(248, 68, 51, 92);
    private static readonly Brush CenterBrush = FrozenBrush(250, 35, 36, 44);
    private static readonly Brush TextBrush = FrozenBrush(255, 250, 250, 252);
    private static readonly Brush MutedBrush = FrozenBrush(255, 184, 179, 192);
    private static readonly Brush SelectedBrush = FrozenBrush(255, 183, 154, 239);
    private static readonly Brush DisabledBrush = FrozenBrush(255, 116, 112, 125);
    private static readonly Pen DividerPen = FrozenPen(FrozenBrush(126, 218, 214, 222), 0.8);
    private static readonly Pen OuterPen = FrozenPen(FrozenBrush(190, 233, 222, 255), 1.15);
    private static readonly Pen CenterPen = FrozenPen(FrozenBrush(155, 218, 214, 222), 1);
    private static readonly Pen GuidePen = FrozenPen(FrozenBrush(225, 183, 154, 239), 1.25);
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Microsoft YaHei UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);
    private static readonly Typeface SymbolTypeface = new("Segoe UI");

    private readonly Dictionary<Direction, ActionOption> _items = [];

    public Point Center { get; set; }

    public Point Anchor { get; set; }

    public double OuterRadius { get; set; } = 130;

    public double InnerRadius { get; set; } = 27;

    public Direction SelectedDirection { get; set; }

    public void SetItems(IReadOnlyDictionary<Direction, ActionKind> actions)
    {
        _items.Clear();
        foreach (var pair in actions)
        {
            _items[pair.Key] = ActionCatalog.Get(pair.Value);
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if ((Anchor - Center).Length > 1)
        {
            drawingContext.DrawLine(GuidePen, Anchor, Center);
            drawingContext.DrawEllipse(SelectedBrush, null, Anchor, 2.5, 2.5);
        }

        drawingContext.DrawEllipse(SurfaceBrush, OuterPen, Center, OuterRadius, OuterRadius);
        DrawSector(drawingContext, Direction.Up, -135, -45);
        DrawSector(drawingContext, Direction.Right, -45, 45);
        DrawSector(drawingContext, Direction.Down, 45, 135);
        DrawSector(drawingContext, Direction.Left, 135, 225);

        drawingContext.DrawEllipse(CenterBrush, CenterPen, Center, InnerRadius, InnerRadius);
        DrawCenteredText(drawingContext, "×", SymbolTypeface, 17, MutedBrush, Center.X, Center.Y - 1);
    }

    private void DrawSector(
        DrawingContext drawingContext,
        Direction direction,
        double startAngle,
        double endAngle)
    {
        var selected = direction == SelectedDirection;
        var geometry = CreateSectorGeometry(startAngle, endAngle);
        drawingContext.DrawGeometry(selected ? SelectedSurfaceBrush : SurfaceBrush, DividerPen, geometry);

        var option = _items.GetValueOrDefault(direction) ?? ActionCatalog.Get(ActionKind.None);
        var angle = DegreesToRadians((startAngle + endAngle) / 2);
        var contentRadius = InnerRadius + ((OuterRadius - InnerRadius) * 0.54);
        var x = Center.X + (Math.Cos(angle) * contentRadius);
        var y = Center.Y + (Math.Sin(angle) * contentRadius);
        var foreground = option.Kind == ActionKind.None
            ? DisabledBrush
            : selected ? SelectedBrush : TextBrush;

        DrawActionIcon(drawingContext, option.Kind, new Point(x, y - 10), foreground);
        DrawCenteredText(drawingContext, option.Label, LabelTypeface, 11.5, foreground, x, y + 19);
    }

    private void DrawActionIcon(DrawingContext context, ActionKind action, Point center, Brush foreground)
    {
        var pen = NewIconPen(foreground);
        switch (action)
        {
            case ActionKind.BringCodex:
                DrawSpark(context, center, pen);
                break;
            case ActionKind.RegionScreenshot:
                DrawCamera(context, center, pen);
                break;
            case ActionKind.ShowDesktop:
                DrawMonitor(context, center, pen);
                break;
            case ActionKind.ClipboardHistory:
                DrawClipboard(context, center, pen);
                break;
            case ActionKind.TaskView:
                DrawTaskView(context, center, pen);
                break;
            case ActionKind.WindowsSearch:
                DrawSearch(context, center, pen);
                break;
            case ActionKind.ToggleMute:
                DrawMute(context, center, pen);
                break;
            case ActionKind.OpenFileExplorer:
                DrawFolder(context, center, pen);
                break;
            case ActionKind.PreviousWindow:
                DrawPreviousWindow(context, center, pen);
                break;
            case ActionKind.EffectCapture:
                DrawEffectCapture(context, center, pen);
                break;
            default:
                context.DrawLine(pen, new Point(center.X - 6, center.Y), new Point(center.X + 6, center.Y));
                context.DrawLine(pen, new Point(center.X, center.Y - 6), new Point(center.X, center.Y + 6));
                break;
        }
    }

    private Geometry CreateSectorGeometry(double startAngle, double endAngle)
    {
        var outerStart = PolarPoint(OuterRadius, startAngle);
        var outerEnd = PolarPoint(OuterRadius, endAngle);
        var innerEnd = PolarPoint(InnerRadius, endAngle);
        var innerStart = PolarPoint(InnerRadius, startAngle);

        var figure = new PathFigure
        {
            StartPoint = innerStart,
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment(outerStart, true));
        figure.Segments.Add(new ArcSegment(
            outerEnd,
            new Size(OuterRadius, OuterRadius),
            0,
            false,
            SweepDirection.Clockwise,
            true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(
            innerStart,
            new Size(InnerRadius, InnerRadius),
            0,
            false,
            SweepDirection.Counterclockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private Point PolarPoint(double radius, double angleDegrees)
    {
        var radians = DegreesToRadians(angleDegrees);
        return new Point(
            Center.X + (Math.Cos(radians) * radius),
            Center.Y + (Math.Sin(radians) * radius));
    }

    private void DrawCenteredText(
        DrawingContext drawingContext,
        string text,
        Typeface typeface,
        double fontSize,
        Brush brush,
        double x,
        double y)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = new FormattedText(
            text,
            CultureInfo.GetCultureInfo("zh-CN"),
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            dpi);
        drawingContext.DrawText(formatted, new Point(x - (formatted.Width / 2), y - (formatted.Height / 2)));
    }

    private static void DrawSpark(DrawingContext context, Point center, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.BeginFigure(new Point(center.X, center.Y - 10), false, true);
            writer.LineTo(new Point(center.X + 2.8, center.Y - 2.8), true, false);
            writer.LineTo(new Point(center.X + 10, center.Y), true, false);
            writer.LineTo(new Point(center.X + 2.8, center.Y + 2.8), true, false);
            writer.LineTo(new Point(center.X, center.Y + 10), true, false);
            writer.LineTo(new Point(center.X - 2.8, center.Y + 2.8), true, false);
            writer.LineTo(new Point(center.X - 10, center.Y), true, false);
            writer.LineTo(new Point(center.X - 2.8, center.Y - 2.8), true, false);
        }

        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
        context.DrawEllipse(null, pen, new Point(center.X + 8, center.Y - 8), 1.5, 1.5);
    }

    private static void DrawCamera(DrawingContext context, Point center, Pen pen)
    {
        context.DrawRoundedRectangle(null, pen, new Rect(center.X - 11, center.Y - 7, 22, 15), 2.5, 2.5);
        context.DrawEllipse(null, pen, center, 4.5, 4.5);
        context.DrawLine(pen, new Point(center.X - 5, center.Y - 7), new Point(center.X - 2.5, center.Y - 10));
        context.DrawLine(pen, new Point(center.X - 2.5, center.Y - 10), new Point(center.X + 2.5, center.Y - 10));
        context.DrawLine(pen, new Point(center.X + 2.5, center.Y - 10), new Point(center.X + 5, center.Y - 7));
    }

    private static void DrawMonitor(DrawingContext context, Point center, Pen pen)
    {
        context.DrawRoundedRectangle(null, pen, new Rect(center.X - 11, center.Y - 9, 22, 15), 2, 2);
        context.DrawLine(pen, new Point(center.X, center.Y + 6), new Point(center.X, center.Y + 10));
        context.DrawLine(pen, new Point(center.X - 6, center.Y + 10), new Point(center.X + 6, center.Y + 10));
    }

    private static void DrawClipboard(DrawingContext context, Point center, Pen pen)
    {
        context.DrawRoundedRectangle(null, pen, new Rect(center.X - 8, center.Y - 9, 16, 19), 2.5, 2.5);
        context.DrawRoundedRectangle(CenterBrush, pen, new Rect(center.X - 4, center.Y - 11, 8, 5), 2, 2);
        context.DrawLine(pen, new Point(center.X - 4, center.Y - 2), new Point(center.X + 4, center.Y - 2));
        context.DrawLine(pen, new Point(center.X - 4, center.Y + 3), new Point(center.X + 4, center.Y + 3));
    }

    private static void DrawTaskView(DrawingContext context, Point center, Pen pen)
    {
        context.DrawRectangle(null, pen, new Rect(center.X - 10, center.Y - 8, 8, 7));
        context.DrawRectangle(null, pen, new Rect(center.X + 2, center.Y - 8, 8, 7));
        context.DrawRectangle(null, pen, new Rect(center.X - 10, center.Y + 3, 8, 7));
        context.DrawRectangle(null, pen, new Rect(center.X + 2, center.Y + 3, 8, 7));
    }

    private static void DrawSearch(DrawingContext context, Point center, Pen pen)
    {
        context.DrawEllipse(null, pen, new Point(center.X - 2, center.Y - 2), 7, 7);
        context.DrawLine(pen, new Point(center.X + 3, center.Y + 3), new Point(center.X + 10, center.Y + 10));
    }

    private static void DrawMute(DrawingContext context, Point center, Pen pen)
    {
        var speaker = new StreamGeometry();
        using (var writer = speaker.Open())
        {
            writer.BeginFigure(new Point(center.X - 10, center.Y - 4), false, true);
            writer.LineTo(new Point(center.X - 5, center.Y - 4), true, false);
            writer.LineTo(new Point(center.X + 1, center.Y - 9), true, false);
            writer.LineTo(new Point(center.X + 1, center.Y + 9), true, false);
            writer.LineTo(new Point(center.X - 5, center.Y + 4), true, false);
            writer.LineTo(new Point(center.X - 10, center.Y + 4), true, false);
        }

        speaker.Freeze();
        context.DrawGeometry(null, pen, speaker);
        context.DrawLine(pen, new Point(center.X + 5, center.Y - 5), new Point(center.X + 11, center.Y + 5));
        context.DrawLine(pen, new Point(center.X + 11, center.Y - 5), new Point(center.X + 5, center.Y + 5));
    }

    private static void DrawFolder(DrawingContext context, Point center, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.BeginFigure(new Point(center.X - 11, center.Y - 7), false, true);
            writer.LineTo(new Point(center.X - 3, center.Y - 7), true, false);
            writer.LineTo(new Point(center.X, center.Y - 4), true, false);
            writer.LineTo(new Point(center.X + 11, center.Y - 4), true, false);
            writer.LineTo(new Point(center.X + 9, center.Y + 8), true, false);
            writer.LineTo(new Point(center.X - 9, center.Y + 8), true, false);
        }

        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawPreviousWindow(DrawingContext context, Point center, Pen pen)
    {
        context.DrawRoundedRectangle(null, pen, new Rect(center.X - 8, center.Y - 10, 16, 13), 2, 2);
        context.DrawRoundedRectangle(null, pen, new Rect(center.X - 4, center.Y - 4, 16, 13), 2, 2);
        context.DrawLine(pen, new Point(center.X - 7, center.Y + 9), new Point(center.X - 12, center.Y + 4));
        context.DrawLine(pen, new Point(center.X - 12, center.Y + 4), new Point(center.X - 7, center.Y - 1));
    }

    private static void DrawEffectCapture(DrawingContext context, Point center, Pen pen)
    {
        const double outer = 10;
        const double corner = 5;
        context.DrawLine(pen, new Point(center.X - outer, center.Y - outer), new Point(center.X - outer + corner, center.Y - outer));
        context.DrawLine(pen, new Point(center.X - outer, center.Y - outer), new Point(center.X - outer, center.Y - outer + corner));
        context.DrawLine(pen, new Point(center.X + outer, center.Y - outer), new Point(center.X + outer - corner, center.Y - outer));
        context.DrawLine(pen, new Point(center.X + outer, center.Y - outer), new Point(center.X + outer, center.Y - outer + corner));
        context.DrawLine(pen, new Point(center.X - outer, center.Y + outer), new Point(center.X - outer + corner, center.Y + outer));
        context.DrawLine(pen, new Point(center.X - outer, center.Y + outer), new Point(center.X - outer, center.Y + outer - corner));
        context.DrawLine(pen, new Point(center.X + outer, center.Y + outer), new Point(center.X + outer - corner, center.Y + outer));
        context.DrawLine(pen, new Point(center.X + outer, center.Y + outer), new Point(center.X + outer, center.Y + outer - corner));
        context.DrawRoundedRectangle(null, pen, new Rect(center.X - 5, center.Y - 3, 10, 7), 1.5, 1.5);
    }

    private static Pen NewIconPen(Brush brush)
    {
        var pen = new Pen(brush, 1.65)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private static Brush FrozenBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}

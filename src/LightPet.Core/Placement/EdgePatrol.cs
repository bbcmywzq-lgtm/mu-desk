namespace LightPet.Core.Placement;

public enum ScreenEdge
{
    Bottom,
    Right,
    Top,
    Left,
}

public static class EdgePatrol
{
    public static int AxisDirection(ScreenEdge edge, int perimeterDirection)
    {
        var clockwise = perimeterDirection < 0 ? -1 : 1;
        return edge switch
        {
            ScreenEdge.Top => clockwise,
            ScreenEdge.Right => clockwise,
            ScreenEdge.Bottom => -clockwise,
            ScreenEdge.Left => -clockwise,
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
    }

    public static ScreenEdge NextEdge(ScreenEdge edge, int perimeterDirection)
    {
        var clockwise = perimeterDirection >= 0;
        return (edge, clockwise) switch
        {
            (ScreenEdge.Top, true) => ScreenEdge.Right,
            (ScreenEdge.Right, true) => ScreenEdge.Bottom,
            (ScreenEdge.Bottom, true) => ScreenEdge.Left,
            (ScreenEdge.Left, true) => ScreenEdge.Top,
            (ScreenEdge.Top, false) => ScreenEdge.Left,
            (ScreenEdge.Left, false) => ScreenEdge.Bottom,
            (ScreenEdge.Bottom, false) => ScreenEdge.Right,
            (ScreenEdge.Right, false) => ScreenEdge.Top,
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
    }

    public static double AvailableDistance(
        ScreenEdge edge,
        int axisDirection,
        double left,
        double top,
        double width,
        double height,
        double workLeft,
        double workTop,
        double workRight,
        double workBottom)
    {
        var direction = axisDirection < 0 ? -1 : 1;
        return edge is ScreenEdge.Top or ScreenEdge.Bottom
            ? direction < 0
                ? Math.Max(0, left - workLeft)
                : Math.Max(0, workRight - (left + width))
            : direction < 0
                ? Math.Max(0, top - workTop)
                : Math.Max(0, workBottom - (top + height));
    }

    public static ScreenEdge? Detect(
        double left,
        double top,
        double width,
        double height,
        double workLeft,
        double workTop,
        double workRight,
        double workBottom,
        double tolerance = 2)
    {
        var candidates = new (ScreenEdge Edge, double Distance)[]
        {
            (ScreenEdge.Bottom, Math.Abs(workBottom - (top + height))),
            (ScreenEdge.Right, Math.Abs(workRight - (left + width))),
            (ScreenEdge.Top, Math.Abs(top - workTop)),
            (ScreenEdge.Left, Math.Abs(left - workLeft)),
        };
        var nearest = candidates.MinBy(candidate => candidate.Distance);
        return nearest.Distance <= Math.Max(0, tolerance) ? nearest.Edge : null;
    }
}

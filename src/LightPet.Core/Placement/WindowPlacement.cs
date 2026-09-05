namespace LightPet.Core.Placement;

public readonly record struct WindowPosition(double Left, double Top);

public readonly record struct PlacementBounds(double Left, double Top, double Right, double Bottom);

public static class WindowPlacement
{
    public static PlacementBounds ExpandForVisibleContent(
        double workLeft,
        double workTop,
        double workRight,
        double workBottom,
        double windowWidth,
        double windowHeight,
        double visibleLeft,
        double visibleTop,
        double visibleRight,
        double visibleBottom,
        double safetyGap = 2)
    {
        var gap = Math.Max(0, safetyGap);
        return new PlacementBounds(
            workLeft - windowWidth * visibleLeft + gap,
            workTop - windowHeight * visibleTop + gap,
            workRight + windowWidth * (1 - visibleRight) - gap,
            workBottom + windowHeight * (1 - visibleBottom) - gap);
    }

    public static WindowPosition Clamp(
        double left,
        double top,
        double width,
        double height,
        double workLeft,
        double workTop,
        double workRight,
        double workBottom)
    {
        var maximumLeft = Math.Max(workLeft, workRight - width);
        var maximumTop = Math.Max(workTop, workBottom - height);
        return new WindowPosition(
            Math.Clamp(left, workLeft, maximumLeft),
            Math.Clamp(top, workTop, maximumTop));
    }

    public static int ChooseWalkDirection(
        int requestedDirection,
        double left,
        double width,
        double workLeft,
        double workRight,
        double desiredDistance)
    {
        var direction = requestedDirection < 0 ? -1 : 1;
        var leftSpace = Math.Max(0, left - workLeft);
        var rightSpace = Math.Max(0, workRight - (left + width));
        var forwardSpace = direction < 0 ? leftSpace : rightSpace;
        var reverseSpace = direction < 0 ? rightSpace : leftSpace;
        return forwardSpace + 0.5 < desiredDistance && reverseSpace > forwardSpace + 12
            ? -direction
            : direction;
    }

    public static double AvailableWalkDistance(
        int direction,
        double left,
        double width,
        double workLeft,
        double workRight,
        double desiredDistance)
    {
        var normalizedDirection = direction < 0 ? -1 : 1;
        var available = normalizedDirection < 0
            ? Math.Max(0, left - workLeft)
            : Math.Max(0, workRight - (left + width));
        return Math.Min(Math.Max(0, desiredDistance), available);
    }
}

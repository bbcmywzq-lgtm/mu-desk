namespace MouseRing.Core;

public static class DirectionResolver
{
    public static Direction Resolve(double deltaX, double deltaY, double cancelRadius)
    {
        var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (distanceSquared <= cancelRadius * cancelRadius)
        {
            return Direction.None;
        }

        if (Math.Abs(deltaX) > Math.Abs(deltaY))
        {
            return deltaX > 0 ? Direction.Right : Direction.Left;
        }

        return deltaY > 0 ? Direction.Down : Direction.Up;
    }
}

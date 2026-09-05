using System.Windows;
using System.Windows.Media.Animation;

namespace PersonalToolbox.Services;

// Only animates a drawing clip. Never changes Window/FrameworkElement layout.
// The analytic spring carries position AND velocity across rapid reversals.
internal sealed class CueRevealAnimation : RectAnimationBase
{
    private const double Frequency = 26;
    internal Rect FromRect { get; init; }
    internal Rect ToRect { get; init; }
    internal RevealVelocity InitialVelocity { get; init; }
    internal const double SettleSeconds = .42;

    internal (Rect Rect, RevealVelocity Velocity) Sample(double seconds)
    {
        var t = Math.Max(0, seconds);
        var x = Axis(FromRect.X, ToRect.X, InitialVelocity.X, t);
        var y = Axis(FromRect.Y, ToRect.Y, InitialVelocity.Y, t);
        var w = Axis(FromRect.Width, ToRect.Width, InitialVelocity.Width, t);
        var h = Axis(FromRect.Height, ToRect.Height, InitialVelocity.Height, t);
        return (new Rect(x.Position, y.Position, Math.Max(1, w.Position), Math.Max(1, h.Position)),
            new RevealVelocity(x.Velocity, y.Velocity, w.Velocity, h.Velocity));
    }
    private static (double Position, double Velocity) Axis(double from, double target, double velocity, double t)
    {
        var offset = from - target;
        var b = velocity + Frequency * offset;
        var decay = Math.Exp(-Frequency * t);
        return (target + (offset + b * t) * decay, (velocity - Frequency * b * t) * decay);
    }
    protected override Rect GetCurrentValueCore(Rect defaultOriginValue, Rect defaultDestinationValue, AnimationClock animationClock)
        => Sample(animationClock.CurrentTime?.TotalSeconds ?? 0).Rect;
    protected override Freezable CreateInstanceCore() => new CueRevealAnimation { FromRect = FromRect, ToRect = ToRect, InitialVelocity = InitialVelocity };
}

internal readonly record struct RevealVelocity(double X, double Y, double Width, double Height);

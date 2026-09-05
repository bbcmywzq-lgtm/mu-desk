using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace PersonalToolbox.Services;

// A continuous tapered ribbon, not overlapping stamps. Coordinates are DIPs;
// time comes from the overlay's monotonic clock, independent of refresh rate.
internal sealed class CueLaserStroke
{
    internal const double Lifetime = .34;
    internal const double MaximumLength = 220;
    private readonly List<(Point Point, double Time)> _samples = [];
    private Point _pointer;
    private double _lastMotion;
    private static readonly SolidColorBrush Glow = Solid(255, 58, 78, 36);
    private static readonly SolidColorBrush Soft = Solid(255, 58, 78, 65);
    private static readonly SolidColorBrush Core = Solid(255, 62, 83, 245);
    private static readonly SolidColorBrush Hot = Solid(255, 221, 216, 235);
    private static readonly RadialGradientBrush Halo = MakeHalo();
    internal int SampleCount => _samples.Count;

    internal void Clear() => _samples.Clear();

    internal void Update(Point pointer, double now)
    {
        _pointer = pointer;
        if (_samples.Count == 0 || (pointer - _samples[^1].Point).Length > .5)
        {
            // Cursor warps should not draw a beam across the whole desktop.
            if (_samples.Count > 0 && (pointer - _samples[^1].Point).Length > 480) _samples.Clear();
            _samples.Add((pointer, now));
            _lastMotion = now;
        }
        while (_samples.Count > 1 && _samples[1].Time <= now - Lifetime) _samples.RemoveAt(0);
        if (_samples.Count > 1 && _samples[0].Time < now - Lifetime)
        {
            var a = _samples[0]; var b = _samples[1];
            var time = now - Lifetime;
            _samples[0] = (a.Point + (b.Point - a.Point) * ((time - a.Time) / (b.Time - a.Time)), time);
        }
        if (_samples.Count == 1 && now - _samples[0].Time >= Lifetime) _samples.Clear();
        var length = 0d;
        for (var i = _samples.Count - 1; i > 0; i--)
        {
            var a = _samples[i - 1]; var b = _samples[i];
            var segment = (b.Point - a.Point).Length;
            if (length + segment > MaximumLength)
            {
                var fraction = (MaximumLength - length) / segment;
                _samples[i - 1] = (b.Point + (a.Point - b.Point) * fraction, b.Time + (a.Time - b.Time) * fraction);
                _samples.RemoveRange(0, i - 1);
                break;
            }
            length += segment;
        }
        if (_samples.Count > 128) _samples.RemoveRange(0, _samples.Count - 128);
    }

    internal void Draw(DrawingContext context, double now)
    {
        if (_samples.Count > 1)
        {
            var points = SmoothPoints(now);
            var fade = Math.Clamp(1 - (now - _lastMotion) / Lifetime, 0, 1);
            fade = fade * fade * (3 - 2 * fade);
            context.PushOpacity(fade);
            DrawRibbon(context, points, 3.2, Glow);
            DrawRibbon(context, points, 1.8, Soft);
            DrawRibbon(context, points, 1, Core);
            DrawRibbon(context, points, .28, Hot);
            context.Pop();
        }
        context.DrawEllipse(Halo, null, _pointer, 9, 9);
        context.DrawEllipse(Core, null, _pointer, 2.8, 2.8);
        context.DrawEllipse(Hot, null, _pointer, 1.15, 1.15);
    }

    private List<(Point Point, double Width)> SmoothPoints(double now)
    {
        var result = new List<(Point Point, double Width)>();
        var arc = 0d;
        for (var i = 0; i < _samples.Count - 1; i++)
        {
            var a = _samples[Math.Max(0, i - 1)].Point;
            var b = _samples[i].Point;
            var c = _samples[i + 1].Point;
            var d = _samples[Math.Min(_samples.Count - 1, i + 2)].Point;
            var segment = (c - b).Length;
            // Bounded tangents round joins without overshooting a sharp reversal.
            var first = Tangent(c - a, segment);
            var second = Tangent(d - b, segment);
            var steps = Math.Clamp((int)Math.Ceiling(segment / 3), 2, 32);
            for (var step = 0; step <= steps; step++)
            {
                if (i > 0 && step == 0) continue;
                var t = (double)step / steps;
                var t2 = t * t; var t3 = t2 * t;
                var point = new Point(
                    (2*t3-3*t2+1)*b.X + (t3-2*t2+t)*first.X + (-2*t3+3*t2)*c.X + (t3-t2)*second.X,
                    (2*t3-3*t2+1)*b.Y + (t3-2*t2+t)*first.Y + (-2*t3+3*t2)*c.Y + (t3-t2)*second.Y);
                if (result.Count > 0) arc += (point - result[^1].Point).Length;
                var time = _samples[i].Time + (_samples[i + 1].Time - _samples[i].Time) * t;
                var life = Math.Clamp(1 - (now - time) / Lifetime, 0, 1);
                var taper = Math.Min(1, arc / 32);
                result.Add((point, 1.65 * Math.Pow(life, .7) * taper));
            }
        }
        return result;
    }

    private static Vector Tangent(Vector value, double length)
    {
        value *= .5;
        return value.Length > length && value.Length > 0 ? value * (length / value.Length) : value;
    }

    private static void DrawRibbon(DrawingContext context, List<(Point Point, double Width)> points, double scale, System.Windows.Media.Brush brush)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(points[0].Point, true, true);
            for (var i = 0; i < points.Count; i++) path.LineTo(Edge(i, 1), true, false);
            for (var i = points.Count - 1; i >= 0; i--) path.LineTo(Edge(i, -1), true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(brush, null, geometry);

        Point Edge(int i, double side)
        {
            var direction = points[Math.Min(points.Count - 1, i + 1)].Point - points[Math.Max(0, i - 1)].Point;
            if (direction.Length < .001) return points[i].Point;
            direction.Normalize();
            return points[i].Point + new Vector(-direction.Y, direction.X) * (points[i].Width * scale * side);
        }
    }

    private static SolidColorBrush Solid(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b)); brush.Freeze(); return brush;
    }
    private static RadialGradientBrush MakeHalo()
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 62, 83), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(48, 255, 62, 83), .4));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 62, 83), 1));
        brush.Freeze(); return brush;
    }
}

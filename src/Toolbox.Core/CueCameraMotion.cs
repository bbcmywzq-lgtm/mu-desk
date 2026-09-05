namespace Toolbox.Core;

/// <summary>Screen-space camera state. Translation and scale share one time-based response.</summary>
public sealed class CueCameraMotion
{
    private Spring _zoom = new(1);
    private Spring _x = new(0);
    private Spring _y = new(0);
    public double Zoom => _zoom.Position;
    public double X => _x.Position;
    public double Y => _y.Position;
    public double ZoomVelocity => _zoom.Velocity;
    public double TargetZoom { get; private set; } = 1;
    public double TargetX { get; private set; }
    public double TargetY { get; private set; }
    public bool IsSettled => Math.Abs(Zoom - TargetZoom) < 0.0001 &&
        Math.Abs(X - TargetX) < 0.05 && Math.Abs(Y - TargetY) < 0.05 &&
        Math.Abs(_zoom.Velocity) < 0.002 && Math.Abs(_x.Velocity) < 1 && Math.Abs(_y.Velocity) < 1;

    public void Aim(double zoom, double x, double y)
    {
        TargetZoom = Math.Clamp(zoom, 1, 5);
        TargetX = x;
        TargetY = y;
    }

    public void AimAt(double zoom, double screenX, double screenY)
    {
        var sourceX = (screenX - X) / Zoom;
        var sourceY = (screenY - Y) / Zoom;
        Aim(zoom, screenX - zoom * sourceX, screenY - zoom * sourceY);
    }

    public void Step(double elapsedSeconds, int responseMilliseconds)
    {
        if (responseMilliseconds == 0)
        {
            _zoom = new(TargetZoom);
            _x = new(TargetX);
            _y = new(TargetY);
            return;
        }
        // Analytic critically damped spring: no frame-count dependency and no
        // velocity reset on retarget. About 95% response within the chosen duration.
        var omega = 4.75 / (Math.Clamp(responseMilliseconds, 40, 500) / 1000d);
        _zoom.Step(TargetZoom, omega, elapsedSeconds);
        _x.Step(TargetX, omega, elapsedSeconds);
        _y.Step(TargetY, omega, elapsedSeconds);
    }

    public void Reset()
    {
        _zoom = new(1); _x = new(0); _y = new(0);
        Aim(1, 0, 0);
    }

    private struct Spring(double position)
    {
        public double Position = position;
        public double Velocity;
        public void Step(double target, double omega, double dt)
        {
            dt = Math.Max(0, dt);
            var delta = Position - target;
            var c = Velocity + omega * delta;
            var decay = Math.Exp(-omega * dt);
            Position = target + (delta + c * dt) * decay;
            Velocity = (Velocity - omega * c * dt) * decay;
        }
    }
}

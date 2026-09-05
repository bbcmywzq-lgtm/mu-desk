namespace Toolbox.Core;

public sealed class CueSettings
{
    public bool Enabled { get; set; }

    public bool FocusHoldEnabled { get; set; } = true;

    public int HoldThresholdMilliseconds { get; set; } = 160;

    public int AnimationMilliseconds { get; set; } = 220;

    public int AnimationTuningVersion { get; set; }

    public double ZoomFactor { get; set; } = 2.0;

    public bool FollowCursor { get; set; } = true;

    public bool PointerRingEnabled { get; set; }

    public bool LaserEnabled { get; set; }

    public bool ClickPulseEnabled { get; set; } = true;

    public bool SpotlightEnabled { get; set; }

    public bool MagnifierEnabled { get; set; }

    public string SpotlightShape { get; set; } = "Circle";

    public double SpotlightRadius { get; set; } = 150;

    public double SpotlightOpacity { get; set; } = 0.58;

    public string MagnifierShape { get; set; } = "Circle";

    public double MagnifierRadius { get; set; } = 240;

    public int LensAppearanceVersion { get; set; }

    public double MagnifierZoom { get; set; } = 2.0;

    public string ScreenshotFolder { get; set; } = string.Empty;

    public void Normalize()
    {
        if (LensAppearanceVersion < 1)
        {
            if (MagnifierRadius == 130) MagnifierRadius = 240;
            LensAppearanceVersion = 1;
        }
        if (AnimationTuningVersion < 1)
        {
            // Migrate the first preview's aggressive default without overriding
            // values the user deliberately chose later.
            if (AnimationMilliseconds == 120)
            {
                AnimationMilliseconds = 220;
            }

            AnimationTuningVersion = 1;
        }

        HoldThresholdMilliseconds = Math.Clamp(HoldThresholdMilliseconds, 80, 500);
        AnimationMilliseconds = Math.Clamp(AnimationMilliseconds, 0, 500);
        ZoomFactor = ClampFinite(ZoomFactor, 1.25, 5.0, 2.0);
        SpotlightRadius = ClampFinite(SpotlightRadius, 60, 500, 150);
        SpotlightOpacity = ClampFinite(SpotlightOpacity, 0.2, 0.9, 0.58);
        MagnifierRadius = ClampFinite(MagnifierRadius, 60, 480, 240);
        MagnifierZoom = ClampFinite(MagnifierZoom, 1.25, 5.0, 2.0);
        SpotlightShape = NormalizeShape(SpotlightShape);
        MagnifierShape = NormalizeShape(MagnifierShape);
        ScreenshotFolder ??= string.Empty;
    }

    private static string NormalizeShape(string? value) =>
        string.Equals(value, "RoundedRectangle", StringComparison.OrdinalIgnoreCase)
            ? "RoundedRectangle"
            : "Circle";

    private static double ClampFinite(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}

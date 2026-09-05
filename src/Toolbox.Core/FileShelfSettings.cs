namespace Toolbox.Core;

public sealed class FileShelfSettings
{
    public bool Enabled { get; set; } = true;

    public string DockSide { get; set; } = "Right";

    public string? DisplayDeviceName { get; set; }

    public double VerticalPosition { get; set; } = 0.5;

    public void Normalize()
    {
        DockSide = string.Equals(DockSide, "Left", StringComparison.OrdinalIgnoreCase)
            ? "Left"
            : "Right";
        DisplayDeviceName = string.IsNullOrWhiteSpace(DisplayDeviceName)
            ? null
            : DisplayDeviceName.Trim();
        if (!double.IsFinite(VerticalPosition))
        {
            VerticalPosition = 0.5;
        }

        VerticalPosition = Math.Clamp(VerticalPosition, 0.08, 0.92);
    }
}

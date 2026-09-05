using LightPet.Core.Interaction;

namespace LightPet.App.Services;

public sealed class AppSettings
{
    public string PackId { get; set; } = "violet-alex-benchmark";

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Size { get; set; } = PetInteractionContract.DefaultDisplaySize;

    public bool ClickThrough { get; set; }

    public bool AutonomousActivity { get; set; } = true;

    public void Normalize(double minimumSize, double maximumSize)
    {
        Size = Math.Clamp(Size, minimumSize, maximumSize);
        if (!double.IsFinite(Left ?? 0))
        {
            Left = null;
        }

        if (!double.IsFinite(Top ?? 0))
        {
            Top = null;
        }
    }
}

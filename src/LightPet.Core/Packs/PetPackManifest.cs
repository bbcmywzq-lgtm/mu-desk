namespace LightPet.Core.Packs;

public sealed class PetPackManifest
{
    public int SchemaVersion { get; init; } = 1;

    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public CanvasDefinition Canvas { get; init; } = new();

    public DisplayDefinition Display { get; init; } = new();

    public List<HitRegionDefinition> HitRegions { get; init; } = [];

    public List<ActionDefinition> Actions { get; init; } = [];
}

public sealed class CanvasDefinition
{
    public int Width { get; init; } = 1000;

    public int Height { get; init; } = 1000;

    public double AnchorX { get; init; } = 500;

    public double AnchorY { get; init; } = 945;
}

public sealed class DisplayDefinition
{
    public double DefaultSize { get; init; } = 130;

    public double MinimumSize { get; init; } = 130;

    public double MaximumSize { get; init; } = 400;

    public VisibleBoundsDefinition VisibleBounds { get; init; } = new();
}

public sealed class VisibleBoundsDefinition
{
    public double Left { get; init; } = 0.25;

    public double Top { get; init; } = 0.055;

    public double Right { get; init; } = 0.75;

    public double Bottom { get; init; } = 0.95;
}

public sealed class HitRegionDefinition
{
    public string Id { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public bool Contains(double x, double y) =>
        x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

public sealed class ActionDefinition
{
    public string Id { get; init; } = string.Empty;

    public List<PhaseDefinition> Phases { get; init; } = [];
}

public sealed class PhaseDefinition
{
    public AnimationPhase Kind { get; init; }

    public string Folder { get; init; } = string.Empty;
}

public enum AnimationPhase
{
    Start,
    Loop,
    End,
    Single,
}

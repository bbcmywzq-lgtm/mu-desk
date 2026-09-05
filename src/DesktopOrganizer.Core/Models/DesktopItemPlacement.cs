namespace DesktopOrganizer.Core.Models;

public sealed class DesktopItemPlacement
{
    public required string Path { get; set; }

    public required string ZoneId { get; set; }

    public int OrderIndex { get; set; }

    public PlacementKind Kind { get; set; }

    public string? RuleId { get; set; }

    public DesktopItemPlacement Clone() => new()
    {
        Path = Path,
        ZoneId = ZoneId,
        OrderIndex = OrderIndex,
        Kind = Kind,
        RuleId = RuleId,
    };
}

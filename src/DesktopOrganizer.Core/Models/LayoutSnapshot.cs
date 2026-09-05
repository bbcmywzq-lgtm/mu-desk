namespace DesktopOrganizer.Core.Models;

public sealed class LayoutSnapshot
{
    public const int CurrentSchemaVersion = 6;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ZoneLayout> Zones { get; set; } = [];

    public List<DesktopItemPlacement> Placements { get; set; } = [];

    public List<OrganizationRule> Rules { get; set; } = [];

    public LayoutSnapshot Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        SavedAt = SavedAt,
        Zones = Zones.Select(zone => zone.Clone()).ToList(),
        Placements = Placements.Select(placement => placement.Clone()).ToList(),
        Rules = Rules.Select(rule => rule.Clone()).ToList(),
    };
}

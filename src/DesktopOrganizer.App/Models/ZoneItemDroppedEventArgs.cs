namespace DesktopOrganizer.Models;

public sealed class ZoneItemDroppedEventArgs(
    string zoneId,
    string sourceZoneId,
    IReadOnlyList<string> paths) : EventArgs
{
    public string ZoneId { get; } = zoneId;

    public string SourceZoneId { get; } = sourceZoneId;

    public IReadOnlyList<string> Paths { get; } = paths;
}

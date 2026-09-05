namespace DesktopOrganizer.Core.Models;

public sealed class OrganizationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "新规则";

    public bool IsEnabled { get; set; } = true;

    public int Priority { get; set; }

    public RuleMatchKind MatchKind { get; set; }

    public string Pattern { get; set; } = string.Empty;

    public string TargetZoneId { get; set; } = string.Empty;

    public OrganizationRule Clone() => new()
    {
        Id = Id,
        Name = Name,
        IsEnabled = IsEnabled,
        Priority = Priority,
        MatchKind = MatchKind,
        Pattern = Pattern,
        TargetZoneId = TargetZoneId,
    };
}

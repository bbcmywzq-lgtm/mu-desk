using DesktopOrganizer.Core.Models;

namespace DesktopOrganizer.Core.Services;

public static class LayoutStateManager
{
    public static void Normalize(LayoutSnapshot snapshot)
    {
        snapshot.SchemaVersion = LayoutSnapshot.CurrentSchemaVersion;
        snapshot.Zones ??= [];
        snapshot.Placements ??= [];
        snapshot.Rules ??= [];

        var desktopZones = snapshot.Zones.Where(zone => zone.Kind == ZoneKind.Desktop).ToArray();
        var inbox = desktopZones.FirstOrDefault(zone => zone.IsInbox);
        if (inbox is null && desktopZones.Length > 0)
        {
            inbox = desktopZones[0];
            inbox.IsInbox = true;
        }

        foreach (var duplicateInbox in snapshot.Zones.Where(zone => zone.IsInbox && zone != inbox))
        {
            duplicateInbox.IsInbox = false;
        }

        var zoneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zone in snapshot.Zones)
        {
            if (string.IsNullOrWhiteSpace(zone.Id) || !zoneIds.Add(zone.Id))
            {
                zone.Id = Guid.NewGuid().ToString("N");
                zoneIds.Add(zone.Id);
            }

            zone.Name = string.IsNullOrWhiteSpace(zone.Name) ? "未命名分区" : zone.Name.Trim();
            zone.SourcePath = zone.SourcePath?.Trim() ?? string.Empty;
            zone.Extensions = zone.Extensions
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Select(NormalizeExtension)
                .Where(extension => extension.Length > 1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (zone.Kind == ZoneKind.SmartFolder)
            {
                zone.IsInbox = false;
            }
        }

        var desktopZoneIds = snapshot.Zones
            .Where(zone => zone.Kind == ZoneKind.Desktop)
            .Select(zone => zone.Id)
            .ToHashSet(StringComparer.Ordinal);

        var deduplicatedPlacements = snapshot.Placements
            .Where(placement =>
                !string.IsNullOrWhiteSpace(placement.Path) &&
                desktopZoneIds.Contains(placement.ZoneId) &&
                (inbox is null || !string.Equals(placement.ZoneId, inbox.Id, StringComparison.Ordinal)))
            .GroupBy(placement => placement.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last().Clone())
            .ToList();
        snapshot.Placements = deduplicatedPlacements;

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in snapshot.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !ruleIds.Add(rule.Id))
            {
                rule.Id = Guid.NewGuid().ToString("N");
                ruleIds.Add(rule.Id);
            }

            rule.Name = string.IsNullOrWhiteSpace(rule.Name) ? "未命名规则" : rule.Name.Trim();
            rule.Pattern = NormalizePattern(rule.MatchKind, rule.Pattern);
            if (string.IsNullOrWhiteSpace(rule.Pattern) ||
                !desktopZoneIds.Contains(rule.TargetZoneId) ||
                (inbox is not null && string.Equals(rule.TargetZoneId, inbox.Id, StringComparison.Ordinal)))
            {
                rule.IsEnabled = false;
            }
        }
    }

    public static bool MoveItem(LayoutSnapshot snapshot, string path, string targetZoneId)
    {
        var targetZone = snapshot.Zones.FirstOrDefault(zone => zone.Id == targetZoneId);
        if (targetZone is null || targetZone.Kind != ZoneKind.Desktop || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var changed = snapshot.Placements.RemoveAll(placement =>
            string.Equals(placement.Path, path, StringComparison.OrdinalIgnoreCase)) > 0;
        if (targetZone.IsInbox)
        {
            return changed;
        }

        var order = snapshot.Placements.Count(placement => placement.ZoneId == targetZone.Id);
        snapshot.Placements.Add(new DesktopItemPlacement
        {
            Path = path,
            ZoneId = targetZone.Id,
            OrderIndex = order,
            Kind = PlacementKind.Manual,
        });
        return true;
    }

    public static bool DeleteZone(LayoutSnapshot snapshot, string zoneId)
    {
        var zone = snapshot.Zones.FirstOrDefault(candidate => candidate.Id == zoneId);
        if (zone is null)
        {
            return false;
        }

        var wasInbox = zone.IsInbox;
        snapshot.Zones.Remove(zone);
        snapshot.Placements.RemoveAll(placement => placement.ZoneId == zoneId);
        snapshot.Rules.RemoveAll(rule => rule.TargetZoneId == zoneId);

        var remainingDesktopZones = snapshot.Zones
            .Where(candidate => candidate.Kind == ZoneKind.Desktop)
            .ToArray();
        if (wasInbox && remainingDesktopZones.Length > 0)
        {
            foreach (var remainingZone in remainingDesktopZones)
            {
                remainingZone.IsInbox = false;
            }

            var replacementInbox = remainingDesktopZones[0];
            replacementInbox.IsInbox = true;
            snapshot.Placements.RemoveAll(placement => placement.ZoneId == replacementInbox.Id);
            foreach (var rule in snapshot.Rules.Where(rule => rule.TargetZoneId == replacementInbox.Id))
            {
                rule.IsEnabled = false;
            }
        }

        return true;
    }

    public static bool RenameItemPath(LayoutSnapshot snapshot, string oldPath, string newPath)
    {
        var placement = snapshot.Placements.LastOrDefault(candidate =>
            string.Equals(candidate.Path, oldPath, StringComparison.OrdinalIgnoreCase));
        if (placement is null || string.IsNullOrWhiteSpace(newPath))
        {
            return false;
        }

        placement.Path = newPath;
        return true;
    }

    public static bool ApplyRules(LayoutSnapshot snapshot, IReadOnlyCollection<DesktopItemDescriptor> items)
    {
        var previous = snapshot.Placements
            .Where(placement => placement.Kind == PlacementKind.Rule)
            .Select(placement => $"{placement.Path}\u001f{placement.ZoneId}\u001f{placement.RuleId}")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        snapshot.Placements.RemoveAll(placement => placement.Kind == PlacementKind.Rule);
        var manualPaths = snapshot.Placements
            .Select(placement => placement.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validTargets = snapshot.Zones
            .Where(zone => zone.Kind == ZoneKind.Desktop && !zone.IsInbox)
            .Select(zone => zone.Id)
            .ToHashSet(StringComparer.Ordinal);
        var rules = snapshot.Rules
            .Where(rule => rule.IsEnabled && validTargets.Contains(rule.TargetZoneId))
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        foreach (var item in items.Where(item => !manualPaths.Contains(item.Path)))
        {
            var rule = rules.FirstOrDefault(candidate => Matches(candidate, item));
            if (rule is null)
            {
                continue;
            }

            snapshot.Placements.Add(new DesktopItemPlacement
            {
                Path = item.Path,
                ZoneId = rule.TargetZoneId,
                OrderIndex = snapshot.Placements.Count(placement => placement.ZoneId == rule.TargetZoneId),
                Kind = PlacementKind.Rule,
                RuleId = rule.Id,
            });
        }

        var current = snapshot.Placements
            .Where(placement => placement.Kind == PlacementKind.Rule)
            .Select(placement => $"{placement.Path}\u001f{placement.ZoneId}\u001f{placement.RuleId}")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return !previous.SequenceEqual(current, StringComparer.OrdinalIgnoreCase);
    }

    private static bool Matches(OrganizationRule rule, DesktopItemDescriptor item)
    {
        var pattern = NormalizePattern(rule.MatchKind, rule.Pattern);
        return rule.MatchKind switch
        {
            RuleMatchKind.Extension => string.Equals(
                Path.GetExtension(item.Name),
                pattern,
                StringComparison.OrdinalIgnoreCase),
            RuleMatchKind.NameContains => item.Name.Contains(
                pattern,
                StringComparison.CurrentCultureIgnoreCase),
            _ => false,
        };
    }

    private static string NormalizePattern(RuleMatchKind matchKind, string? pattern)
    {
        var normalized = pattern?.Trim() ?? string.Empty;
        if (matchKind == RuleMatchKind.Extension && normalized.Length > 0 && normalized[0] != '.')
        {
            normalized = $".{normalized}";
        }

        return normalized;
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.')
            ? trimmed.ToLowerInvariant()
            : $".{trimmed.ToLowerInvariant()}";
    }
}

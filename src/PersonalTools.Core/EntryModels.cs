namespace PersonalTools.Core;

public enum EntryStatus
{
    Active,
    Completed,
    Archived,
}

public enum RepeatRule
{
    None,
    Daily,
    Weekdays,
    Weekly,
}

public sealed record DesktopCardState(
    bool IsPinned = false,
    double Left = 80,
    double Top = 80,
    double Width = 300,
    double Height = 210,
    string Color = "violet",
    double Opacity = 0.96,
    bool AlwaysOnTop = false,
    bool IsCollapsed = false,
    bool IsLocked = false,
    string? MonitorId = null);

public sealed record EntryItem(
    string Id,
    string ProviderId,
    string? ExternalId,
    string Source,
    IReadOnlyDictionary<string, string> Metadata,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    EntryStatus Status,
    string Content,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    DateTimeOffset? DueAt,
    DateTimeOffset? LastTriggeredAt,
    RepeatRule Repeat,
    DesktopCardState DesktopCard);

public sealed record CreateEntryCommand(
    string Content,
    IReadOnlyList<string>? Tags = null,
    DateTimeOffset? DueAt = null,
    bool PinToDesktop = false,
    RepeatRule Repeat = RepeatRule.None,
    string Source = "reminder-notes",
    string? ExternalId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record UpdateEntryCommand(
    string Content,
    IReadOnlyList<string>? Tags = null,
    DateTimeOffset? DueAt = null,
    bool ClearDueAt = false,
    bool? IsFavorite = null,
    RepeatRule? Repeat = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record EntryQuery(
    string? Text = null,
    bool IncludeCompleted = true,
    bool IncludeArchived = false,
    bool? PinnedToDesktop = null,
    DateTimeOffset? DueBefore = null,
    int Limit = 500);

namespace PersonalTools.Core;

public enum ReminderStatus
{
    Pending,
    Completed,
}

public enum NoteStatus
{
    Active,
    Archived,
}

public sealed record ReminderItem(
    string Id,
    string ProviderId,
    string? ExternalId,
    string Source,
    IReadOnlyDictionary<string, string> Metadata,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset DueAt,
    DateTimeOffset? LastTriggeredAt,
    ReminderStatus Status,
    string Content);

public sealed record NoteItem(
    string Id,
    string ProviderId,
    string? ExternalId,
    string Source,
    IReadOnlyDictionary<string, string> Metadata,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    NoteStatus Status,
    string? Title,
    string Content,
    IReadOnlyList<string> Tags);

public sealed record CreateReminderCommand(
    string Content,
    DateTimeOffset DueAt,
    string Source = "desktop-pet",
    string? ExternalId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record CreateNoteCommand(
    string Content,
    string? Title = null,
    IReadOnlyList<string>? Tags = null,
    string Source = "desktop-pet",
    string? ExternalId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record UpdateNoteCommand(
    string Content,
    string? Title = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record NoteSearchQuery(
    string? Text = null,
    IReadOnlyList<string>? Tags = null,
    bool IncludeArchived = false,
    int Limit = 100);

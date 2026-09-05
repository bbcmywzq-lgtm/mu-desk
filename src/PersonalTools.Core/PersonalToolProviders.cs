namespace PersonalTools.Core;

/// <summary>
/// A reminder backend. Every returned item belongs exclusively to <see cref="ProviderId"/>.
/// Implementations may be local or adapters for a dedicated external tool.
/// </summary>
public interface IReminderProvider
{
    string ProviderId { get; }

    Task<ReminderItem> CreateAsync(
        CreateReminderCommand command,
        CancellationToken cancellationToken = default);

    Task<ReminderItem?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReminderItem>> GetPendingAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReminderItem>> GetMissedAsync(
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);

    Task<ReminderItem> CompleteAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<ReminderItem> SnoozeAsync(
        string id,
        DateTimeOffset newDueAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically records that a still-pending reminder was presented. The expected
    /// revision prevents a stale scheduler snapshot from reviving a completed or
    /// snoozed reminder. A missing, changed, future or previously triggered item
    /// returns <see langword="null"/>.
    /// </summary>
    Task<ReminderItem?> TryMarkTriggeredIfDueAsync(
        string id,
        long expectedRevision,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A note backend. UI code should depend on this contract rather than a storage format.
/// </summary>
public interface INoteProvider
{
    string ProviderId { get; }

    Task<NoteItem> CreateAsync(
        CreateNoteCommand command,
        CancellationToken cancellationToken = default);

    Task<NoteItem?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<NoteItem> UpdateAsync(
        string id,
        UpdateNoteCommand command,
        CancellationToken cancellationToken = default);

    Task<NoteItem> ArchiveAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NoteItem>> SearchAsync(
        NoteSearchQuery query,
        CancellationToken cancellationToken = default);
}

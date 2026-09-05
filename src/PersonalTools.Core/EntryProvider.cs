namespace PersonalTools.Core;

public interface IEntryProvider
{
    string ProviderId { get; }

    Task<EntryItem> CreateAsync(CreateEntryCommand command, CancellationToken cancellationToken = default);

    Task<EntryItem?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntryItem>> QueryAsync(EntryQuery query, CancellationToken cancellationToken = default);

    Task<EntryItem> UpdateAsync(string id, UpdateEntryCommand command, CancellationToken cancellationToken = default);

    Task<EntryItem> SetStatusAsync(string id, EntryStatus status, CancellationToken cancellationToken = default);

    Task<EntryItem> CompleteAsync(string id, CancellationToken cancellationToken = default);

    Task<EntryItem> SnoozeAsync(string id, DateTimeOffset newDueAt, CancellationToken cancellationToken = default);

    Task<EntryItem> SetDesktopCardAsync(string id, DesktopCardState state, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntryItem>> GetMissedAsync(DateTimeOffset? asOf = null, CancellationToken cancellationToken = default);

    Task<EntryItem?> TryMarkTriggeredIfDueAsync(
        string id,
        long expectedRevision,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

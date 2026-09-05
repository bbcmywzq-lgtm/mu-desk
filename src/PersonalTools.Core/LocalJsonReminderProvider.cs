using System.Collections.ObjectModel;

namespace PersonalTools.Core;

public sealed class LocalJsonReminderProvider : IReminderProvider
{
    private const int SchemaVersion = 1;
    private readonly TimeProvider _clock;
    private readonly AtomicJsonFile<ReminderDocument> _store;

    public LocalJsonReminderProvider(
        string filePath,
        string providerId = "local",
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId.Trim();
        _clock = clock ?? TimeProvider.System;
        _store = new AtomicJsonFile<ReminderDocument>(
            filePath,
            () => new ReminderDocument(SchemaVersion, []),
            document => IsValidDocument(document, ProviderId));
    }

    public string ProviderId { get; }

    public Task<ReminderItem> CreateAsync(
        CreateReminderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var content = Required(command.Content, nameof(command.Content));
        var source = Required(command.Source, nameof(command.Source));
        var externalId = Optional(command.ExternalId);
        var metadata = CopyMetadata(command.Metadata);
        var now = _clock.GetUtcNow();
        var item = new ReminderItem(
            Guid.NewGuid().ToString("N"),
            ProviderId,
            externalId,
            source,
            metadata,
            Revision: 1,
            CreatedAt: now,
            UpdatedAt: now,
            DueAt: command.DueAt.ToUniversalTime(),
            LastTriggeredAt: null,
            Status: ReminderStatus.Pending,
            Content: content);

        return _store.UpdateAsync(document =>
        {
            EnsureExternalIdAvailable(document.Items, externalId);
            var next = document.Items.Append(item).ToArray();
            return (document with { Items = next }, Clone(item));
        }, cancellationToken);
    }

    public Task<ReminderItem?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        id = Required(id, nameof(id));
        return _store.ReadAsync(document =>
        {
            var item = document.Items.FirstOrDefault(candidate => candidate.Id == id);
            return item is null ? null : Clone(item);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ReminderItem>> GetPendingAsync(
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync(document => Snapshot(document.Items
            .Where(item => item.Status == ReminderStatus.Pending)
            .OrderBy(item => item.DueAt)), cancellationToken);

    public Task<IReadOnlyList<ReminderItem>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync(document => Snapshot(document.Items
            .OrderByDescending(item => item.UpdatedAt)), cancellationToken);

    public Task<IReadOnlyList<ReminderItem>> GetMissedAsync(
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var threshold = (asOf ?? _clock.GetUtcNow()).ToUniversalTime();
        return _store.ReadAsync(document => Snapshot(document.Items
            .Where(item =>
                item.Status == ReminderStatus.Pending &&
                item.DueAt <= threshold &&
                item.LastTriggeredAt is null)
            .OrderBy(item => item.DueAt)), cancellationToken);
    }

    public Task<ReminderItem> CompleteAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(id, item => item with
        {
            Status = ReminderStatus.Completed,
            Revision = item.Revision + 1,
            UpdatedAt = _clock.GetUtcNow(),
        }, cancellationToken);

    public Task<ReminderItem> SnoozeAsync(
        string id,
        DateTimeOffset newDueAt,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var dueAt = newDueAt.ToUniversalTime();
        if (dueAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(newDueAt), "A snoozed reminder must be due in the future.");
        }

        return ChangeAsync(id, item => item with
        {
            Status = ReminderStatus.Pending,
            DueAt = dueAt,
            LastTriggeredAt = null,
            Revision = item.Revision + 1,
            UpdatedAt = now,
        }, cancellationToken);
    }

    public Task<ReminderItem?> TryMarkTriggeredIfDueAsync(
        string id,
        long expectedRevision,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        id = Required(id, nameof(id));
        if (expectedRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }

        var now = _clock.GetUtcNow();
        var threshold = asOf.ToUniversalTime();
        return _store.UpdateAsync(document =>
        {
            var index = Array.FindIndex(document.Items, item => item.Id == id);
            if (index < 0)
            {
                return (document, (ReminderItem?)null);
            }

            var item = document.Items[index];
            if (item.Status != ReminderStatus.Pending ||
                item.Revision != expectedRevision ||
                item.DueAt > threshold ||
                item.LastTriggeredAt is not null)
            {
                return (document, (ReminderItem?)null);
            }

            var changed = item with
            {
                LastTriggeredAt = now,
                Revision = item.Revision + 1,
                UpdatedAt = now,
            };
            var next = (ReminderItem[])document.Items.Clone();
            next[index] = changed;
            return (document with { Items = next }, (ReminderItem?)Clone(changed));
        }, cancellationToken);
    }

    public Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        id = Required(id, nameof(id));
        return _store.UpdateAsync(document =>
        {
            var next = document.Items.Where(item => item.Id != id).ToArray();
            return (document with { Items = next }, next.Length != document.Items.Length);
        }, cancellationToken);
    }

    private Task<ReminderItem> ChangeAsync(
        string id,
        Func<ReminderItem, ReminderItem> change,
        CancellationToken cancellationToken)
    {
        id = Required(id, nameof(id));
        return _store.UpdateAsync(document =>
        {
            var index = Array.FindIndex(document.Items, item => item.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Reminder '{id}' was not found.");
            }

            var changed = change(document.Items[index]);
            var next = (ReminderItem[])document.Items.Clone();
            next[index] = changed;
            return (document with { Items = next }, Clone(changed));
        }, cancellationToken);
    }

    private static bool IsValidDocument(ReminderDocument document, string providerId) =>
        document.SchemaVersion == SchemaVersion &&
        document.Items is not null &&
        document.Items.All(item =>
            item is not null &&
            item.ProviderId == providerId &&
            !string.IsNullOrWhiteSpace(item.Id) &&
            item.Revision > 0);

    private static void EnsureExternalIdAvailable(ReminderItem[] items, string? externalId)
    {
        if (externalId is not null && items.Any(item => item.ExternalId == externalId))
        {
            throw new InvalidOperationException($"External reminder '{externalId}' already exists in this provider.");
        }
    }

    private static IReadOnlyList<ReminderItem> Snapshot(IEnumerable<ReminderItem> items) =>
        items.Select(Clone).ToArray();

    private static ReminderItem Clone(ReminderItem item) =>
        item with { Metadata = CopyMetadata(item.Metadata) };

    private static IReadOnlyDictionary<string, string> CopyMetadata(
        IReadOnlyDictionary<string, string>? metadata) =>
        new ReadOnlyDictionary<string, string>(
            metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(metadata, StringComparer.Ordinal));

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ReminderDocument(int SchemaVersion, ReminderItem[] Items);
}

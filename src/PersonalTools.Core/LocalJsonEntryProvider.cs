using System.Collections.ObjectModel;

namespace PersonalTools.Core;

public sealed class LocalJsonEntryProvider : IEntryProvider
{
    private const int SchemaVersion = 1;
    private readonly TimeProvider _clock;
    private readonly AtomicJsonFile<EntryDocument> _store;

    public LocalJsonEntryProvider(
        string filePath,
        string providerId = "local-entries",
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId.Trim();
        _clock = clock ?? TimeProvider.System;
        _store = new AtomicJsonFile<EntryDocument>(
            filePath,
            () => new EntryDocument(SchemaVersion, []),
            document => IsValidDocument(document, ProviderId));
    }

    public string ProviderId { get; }

    public Task<EntryItem> CreateAsync(
        CreateEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = _clock.GetUtcNow();
        var externalId = Optional(command.ExternalId);
        var item = new EntryItem(
            Guid.NewGuid().ToString("N"),
            ProviderId,
            externalId,
            Required(command.Source, nameof(command.Source)),
            CopyMetadata(command.Metadata),
            Revision: 1,
            CreatedAt: now,
            UpdatedAt: now,
            Status: EntryStatus.Active,
            Content: Required(command.Content, nameof(command.Content)),
            Tags: NormalizeTags(command.Tags),
            IsFavorite: false,
            DueAt: NormalizeDue(command.DueAt),
            LastTriggeredAt: null,
            Repeat: command.DueAt is null ? RepeatRule.None : command.Repeat,
            DesktopCard: NormalizeCard(new DesktopCardState(IsPinned: command.PinToDesktop)));

        return _store.UpdateAsync(document =>
        {
            if (externalId is not null && document.Items.Any(entry => entry.ExternalId == externalId))
            {
                throw new InvalidOperationException($"External entry '{externalId}' already exists.");
            }

            return (document with { Items = document.Items.Append(item).ToArray() }, Clone(item));
        }, cancellationToken);
    }

    public Task<EntryItem?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        id = Required(id, nameof(id));
        return _store.ReadAsync(document =>
        {
            var item = document.Items.FirstOrDefault(candidate => candidate.Id == id);
            return item is null ? null : Clone(item);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<EntryItem>> QueryAsync(
        EntryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 2000)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Query limit must be between 1 and 2000.");
        }

        var text = Optional(query.Text);
        var dueBefore = query.DueBefore?.ToUniversalTime();
        return _store.ReadAsync(document =>
        {
            IEnumerable<EntryItem> matches = document.Items;
            if (!query.IncludeArchived)
            {
                matches = matches.Where(item => item.Status != EntryStatus.Archived);
            }
            if (!query.IncludeCompleted)
            {
                matches = matches.Where(item => item.Status != EntryStatus.Completed);
            }
            if (query.PinnedToDesktop is { } pinned)
            {
                matches = matches.Where(item => item.DesktopCard.IsPinned == pinned);
            }
            if (dueBefore is { } threshold)
            {
                matches = matches.Where(item => item.DueAt is { } dueAt && dueAt <= threshold);
            }
            if (text is not null)
            {
                matches = matches.Where(item =>
                    item.Content.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    item.Tags.Any(tag => tag.Contains(text, StringComparison.OrdinalIgnoreCase)));
            }

            return (IReadOnlyList<EntryItem>)matches
                .OrderByDescending(item => item.IsFavorite)
                .ThenBy(item => item.Status == EntryStatus.Completed)
                .ThenBy(item => item.DueAt ?? DateTimeOffset.MaxValue)
                .ThenByDescending(item => item.UpdatedAt)
                .Take(query.Limit)
                .Select(Clone)
                .ToArray();
        }, cancellationToken);
    }

    public Task<EntryItem> UpdateAsync(
        string id,
        UpdateEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ChangeAsync(id, item => item with
        {
            Content = Required(command.Content, nameof(command.Content)),
            Tags = command.Tags is null ? item.Tags : NormalizeTags(command.Tags),
            DueAt = command.ClearDueAt ? null : command.DueAt is null ? item.DueAt : NormalizeDue(command.DueAt),
            LastTriggeredAt = command.ClearDueAt || command.DueAt is not null ? null : item.LastTriggeredAt,
            IsFavorite = command.IsFavorite ?? item.IsFavorite,
            Repeat = command.ClearDueAt ? RepeatRule.None : command.Repeat ?? item.Repeat,
            Metadata = command.Metadata is null ? item.Metadata : CopyMetadata(command.Metadata),
            Revision = item.Revision + 1,
            UpdatedAt = _clock.GetUtcNow(),
        }, cancellationToken);
    }

    public Task<EntryItem> SetStatusAsync(
        string id,
        EntryStatus status,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(id, item => item with
        {
            Status = status,
            Revision = item.Revision + 1,
            UpdatedAt = _clock.GetUtcNow(),
        }, cancellationToken);

    public Task<EntryItem> CompleteAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(id, item =>
        {
            var now = _clock.GetUtcNow();
            if (item.Repeat == RepeatRule.None || item.DueAt is null)
            {
                return item with
                {
                    Status = EntryStatus.Completed,
                    Revision = item.Revision + 1,
                    UpdatedAt = now,
                };
            }

            var next = NextOccurrence(item.DueAt.Value, item.Repeat, now);
            return item with
            {
                Status = EntryStatus.Active,
                DueAt = next,
                LastTriggeredAt = null,
                Revision = item.Revision + 1,
                UpdatedAt = now,
            };
        }, cancellationToken);

    public Task<EntryItem> SnoozeAsync(
        string id,
        DateTimeOffset newDueAt,
        CancellationToken cancellationToken = default)
    {
        var dueAt = newDueAt.ToUniversalTime();
        if (dueAt <= _clock.GetUtcNow())
        {
            throw new ArgumentOutOfRangeException(nameof(newDueAt));
        }

        return ChangeAsync(id, item => item with
        {
            Status = EntryStatus.Active,
            DueAt = dueAt,
            LastTriggeredAt = null,
            Revision = item.Revision + 1,
            UpdatedAt = _clock.GetUtcNow(),
        }, cancellationToken);
    }

    public Task<EntryItem> SetDesktopCardAsync(
        string id,
        DesktopCardState state,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(id, item => item with
        {
            DesktopCard = NormalizeCard(state),
            Revision = item.Revision + 1,
            UpdatedAt = _clock.GetUtcNow(),
        }, cancellationToken);

    public Task<IReadOnlyList<EntryItem>> GetMissedAsync(
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var threshold = (asOf ?? _clock.GetUtcNow()).ToUniversalTime();
        return _store.ReadAsync(document => (IReadOnlyList<EntryItem>)document.Items
            .Where(item =>
                item.Status == EntryStatus.Active &&
                item.DueAt is { } dueAt && dueAt <= threshold &&
                item.LastTriggeredAt is null)
            .OrderBy(item => item.DueAt)
            .Select(Clone)
            .ToArray(), cancellationToken);
    }

    public Task<EntryItem?> TryMarkTriggeredIfDueAsync(
        string id,
        long expectedRevision,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        id = Required(id, nameof(id));
        var threshold = asOf.ToUniversalTime();
        return _store.UpdateAsync(document =>
        {
            var index = Array.FindIndex(document.Items, item => item.Id == id);
            if (index < 0)
            {
                return (document, (EntryItem?)null);
            }

            var item = document.Items[index];
            if (item.Status != EntryStatus.Active ||
                item.Revision != expectedRevision ||
                item.DueAt is not { } dueAt || dueAt > threshold ||
                item.LastTriggeredAt is not null)
            {
                return (document, (EntryItem?)null);
            }

            var changed = item with
            {
                LastTriggeredAt = _clock.GetUtcNow(),
                Revision = item.Revision + 1,
                UpdatedAt = _clock.GetUtcNow(),
            };
            var next = (EntryItem[])document.Items.Clone();
            next[index] = changed;
            return (document with { Items = next }, (EntryItem?)Clone(changed));
        }, cancellationToken);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        id = Required(id, nameof(id));
        return _store.UpdateAsync(document =>
        {
            var next = document.Items.Where(item => item.Id != id).ToArray();
            return (document with { Items = next }, next.Length != document.Items.Length);
        }, cancellationToken);
    }

    private Task<EntryItem> ChangeAsync(
        string id,
        Func<EntryItem, EntryItem> change,
        CancellationToken cancellationToken)
    {
        id = Required(id, nameof(id));
        return _store.UpdateAsync(document =>
        {
            var index = Array.FindIndex(document.Items, item => item.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Entry '{id}' was not found.");
            }

            var changed = change(document.Items[index]);
            var next = (EntryItem[])document.Items.Clone();
            next[index] = changed;
            return (document with { Items = next }, Clone(changed));
        }, cancellationToken);
    }

    private static bool IsValidDocument(EntryDocument document, string providerId) =>
        document.SchemaVersion == SchemaVersion &&
        document.Items is not null &&
        document.Items.All(item =>
            item is not null &&
            item.ProviderId == providerId &&
            !string.IsNullOrWhiteSpace(item.Id) &&
            item.Revision > 0 &&
            item.DesktopCard is not null);

    private static EntryItem Clone(EntryItem item) => item with
    {
        Metadata = CopyMetadata(item.Metadata),
        Tags = item.Tags.ToArray(),
        DesktopCard = item.DesktopCard with { },
    };

    private static DesktopCardState NormalizeCard(DesktopCardState state) => state with
    {
        Width = Math.Clamp(state.Width, 230, 680),
        Height = Math.Clamp(state.Height, 120, 620),
        Opacity = Math.Clamp(state.Opacity, 0.55, 1),
        Color = string.IsNullOrWhiteSpace(state.Color) ? "violet" : state.Color.Trim().ToLowerInvariant(),
    };

    private static DateTimeOffset? NormalizeDue(DateTimeOffset? dueAt) => dueAt?.ToUniversalTime();

    private static DateTimeOffset NextOccurrence(
        DateTimeOffset dueAt,
        RepeatRule repeat,
        DateTimeOffset now)
    {
        var local = dueAt.ToLocalTime();
        var candidate = local;
        do
        {
            candidate = repeat switch
            {
                RepeatRule.Daily => candidate.AddDays(1),
                RepeatRule.Weekly => candidate.AddDays(7),
                RepeatRule.Weekdays => NextWeekday(candidate),
                _ => candidate,
            };
        }
        while (candidate.ToUniversalTime() <= now);
        return candidate.ToUniversalTime();
    }

    private static DateTimeOffset NextWeekday(DateTimeOffset value)
    {
        var next = value.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }
        return next;
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags) =>
        tags is null
            ? []
            : tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray();

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

    private sealed record EntryDocument(int SchemaVersion, EntryItem[] Items);
}

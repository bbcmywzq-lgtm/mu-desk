using System.Collections.ObjectModel;

namespace PersonalTools.Core;

public sealed class LocalJsonNoteProvider : INoteProvider
{
    private const int SchemaVersion = 1;
    private readonly TimeProvider _clock;
    private readonly AtomicJsonFile<NoteDocument> _store;

    public LocalJsonNoteProvider(
        string filePath,
        string providerId = "local",
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId.Trim();
        _clock = clock ?? TimeProvider.System;
        _store = new AtomicJsonFile<NoteDocument>(
            filePath,
            () => new NoteDocument(SchemaVersion, []),
            document => IsValidDocument(document, ProviderId));
    }

    public string ProviderId { get; }

    public Task<NoteItem> CreateAsync(
        CreateNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = _clock.GetUtcNow();
        var externalId = Optional(command.ExternalId);
        var item = new NoteItem(
            Guid.NewGuid().ToString("N"),
            ProviderId,
            externalId,
            Required(command.Source, nameof(command.Source)),
            CopyMetadata(command.Metadata),
            Revision: 1,
            CreatedAt: now,
            UpdatedAt: now,
            Status: NoteStatus.Active,
            Title: Optional(command.Title),
            Content: Required(command.Content, nameof(command.Content)),
            Tags: NormalizeTags(command.Tags));

        return _store.UpdateAsync(document =>
        {
            EnsureExternalIdAvailable(document.Items, externalId);
            return (document with { Items = document.Items.Append(item).ToArray() }, Clone(item));
        }, cancellationToken);
    }

    public Task<NoteItem?> GetAsync(
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

    public Task<NoteItem> UpdateAsync(
        string id,
        UpdateNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ChangeAsync(id, item => item with
        {
            Content = Required(command.Content, nameof(command.Content)),
            Title = Optional(command.Title),
            Tags = command.Tags is null ? item.Tags : NormalizeTags(command.Tags),
            Metadata = command.Metadata is null ? item.Metadata : CopyMetadata(command.Metadata),
            Revision = item.Revision + 1,
            UpdatedAt = _clock.GetUtcNow(),
        }, cancellationToken);
    }

    public Task<NoteItem> ArchiveAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(id, item => item with
        {
            Status = NoteStatus.Archived,
            Revision = item.Revision + 1,
            UpdatedAt = _clock.GetUtcNow(),
        }, cancellationToken);

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

    public Task<IReadOnlyList<NoteItem>> SearchAsync(
        NoteSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Search limit must be between 1 and 1000.");
        }

        var text = Optional(query.Text);
        var tags = NormalizeTags(query.Tags);
        return _store.ReadAsync(document =>
        {
            IEnumerable<NoteItem> matches = document.Items;
            if (!query.IncludeArchived)
            {
                matches = matches.Where(item => item.Status == NoteStatus.Active);
            }

            if (text is not null)
            {
                matches = matches.Where(item =>
                    item.Content.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    (item.Title?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            if (tags.Count > 0)
            {
                matches = matches.Where(item => tags.All(tag =>
                    item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
            }

            return (IReadOnlyList<NoteItem>)matches
                .OrderByDescending(item => item.UpdatedAt)
                .Take(query.Limit)
                .Select(Clone)
                .ToArray();
        }, cancellationToken);
    }

    private Task<NoteItem> ChangeAsync(
        string id,
        Func<NoteItem, NoteItem> change,
        CancellationToken cancellationToken)
    {
        id = Required(id, nameof(id));
        return _store.UpdateAsync(document =>
        {
            var index = Array.FindIndex(document.Items, item => item.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Note '{id}' was not found.");
            }

            var changed = change(document.Items[index]);
            var next = (NoteItem[])document.Items.Clone();
            next[index] = changed;
            return (document with { Items = next }, Clone(changed));
        }, cancellationToken);
    }

    private static bool IsValidDocument(NoteDocument document, string providerId) =>
        document.SchemaVersion == SchemaVersion &&
        document.Items is not null &&
        document.Items.All(item =>
            item is not null &&
            item.ProviderId == providerId &&
            !string.IsNullOrWhiteSpace(item.Id) &&
            item.Revision > 0);

    private static void EnsureExternalIdAvailable(NoteItem[] items, string? externalId)
    {
        if (externalId is not null && items.Any(item => item.ExternalId == externalId))
        {
            throw new InvalidOperationException($"External note '{externalId}' already exists in this provider.");
        }
    }

    private static NoteItem Clone(NoteItem item) => item with
    {
        Metadata = CopyMetadata(item.Metadata),
        Tags = item.Tags.ToArray(),
    };

    private static IReadOnlyDictionary<string, string> CopyMetadata(
        IReadOnlyDictionary<string, string>? metadata) =>
        new ReadOnlyDictionary<string, string>(
            metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(metadata, StringComparer.Ordinal));

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags) =>
        tags is null
            ? []
            : tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record NoteDocument(int SchemaVersion, NoteItem[] Items);
}

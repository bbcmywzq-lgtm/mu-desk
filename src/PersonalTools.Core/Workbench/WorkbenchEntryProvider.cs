using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalTools.Core;

/// <summary>工作台事项在本机的镜像（含本机专属的桌面卡片状态）。</summary>
public sealed class WorkbenchCacheEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "task";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("startTime")]
    public string? StartTime { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "todo";

    [JsonPropertyName("dueAt")]
    public string? DueAt { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("favorite")]
    public bool Favorite { get; set; }

    [JsonPropertyName("recurrence")]
    public string Recurrence { get; set; } = "none";

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;

    /// <summary>提醒规则触发时间（来自工作台提醒规则，驱动本机弹窗）。</summary>
    [JsonPropertyName("reminderAt")]
    public string? ReminderAt { get; set; }

    [JsonPropertyName("reminderRepeat")]
    public string ReminderRepeat { get; set; } = "none";

    [JsonPropertyName("lastTriggeredAt")]
    public string? LastTriggeredAt { get; set; }

    /// <summary>本地尚未同步到工作台的标记（离线新建时置位）。</summary>
    [JsonPropertyName("pendingSync")]
    public bool PendingSync { get; set; }

    [JsonPropertyName("desktopCard")]
    public DesktopCardState? DesktopCard { get; set; }
}

public sealed class WorkbenchCache
{
    [JsonPropertyName("entries")]
    public List<WorkbenchCacheEntry> Entries { get; set; } = new();
}

/// <summary>
/// 工作台版事项存储：读本地镜像，写走工作台同步接口，断网先入待发队列。
/// </summary>
public sealed class WorkbenchEntryProvider : IEntryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly TimeZoneInfo WorkbenchZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    private readonly WorkbenchClient _client;
    private readonly string _cachePath;
    private WorkbenchCache _cache = new();
    private bool _cacheLoaded;
    private readonly object _gate = new();

    public WorkbenchEntryProvider(
        WorkbenchClient client,
        string cachePath)
    {
        _client = client;
        _cachePath = cachePath;
    }

    public string ProviderId => "workbench";

    public event EventHandler? CacheChanged;

    public async Task LoadCacheAsync()
    {
        if (File.Exists(_cachePath))
        {
            var json = await File.ReadAllTextAsync(_cachePath);
            _cache =
                JsonSerializer.Deserialize<WorkbenchCache>(json, JsonOptions)
                ?? new WorkbenchCache();
        }
        _cacheLoaded = true;
    }

    private async Task SaveCacheAsync()
    {
        var json = JsonSerializer.Serialize(_cache, JsonOptions);
        await File.WriteAllTextAsync(_cachePath, json);
    }

    /// <summary>从工作台拉全量快照并合并进镜像；本地未同步条目保留，不会覆盖。</summary>
    /// <summary>从工作台拉全量快照并合并进镜像；本地未同步条目保留，不会被覆盖。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _client.FetchSnapshotAsync(cancellationToken);
        List<WorkbenchCacheEntry> locals;
        lock (_gate)
        {
            locals = _cache.Entries.ToList();
        }
        var pendingIds = new HashSet<string>(
            locals.Where(entry => entry.PendingSync).Select(entry => entry.Id));
        lock (_gate)
        {
            var reminders = snapshot.Reminders
                .Select(rule => new
                {
                    entryId = rule.TryGetProperty("entryId", out var entryId)
                        ? entryId.GetString()
                        : null,
                    triggerAt = rule.TryGetProperty("triggerAt", out var triggerAt)
                        ? triggerAt.GetString()
                        : null,
                    repeat = rule.TryGetProperty("repeat", out var repeat)
                        ? repeat.GetString()
                        : "none",
                    deleted = rule.TryGetProperty("deleted", out var deleted)
                        && deleted.ValueKind == JsonValueKind.True,
                })
                .Where(rule => !string.IsNullOrWhiteSpace(rule.entryId))
                .GroupBy(rule => rule.entryId!)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last(rule => !rule.deleted));
            var previousById = locals
                .GroupBy(entry => entry.Id)
                .ToDictionary(group => group.Key, group => group.Last());
            var merged = snapshot.Entries
                .Select(element => element.Deserialize<WorkbenchCacheEntry>(JsonOptions))
                .Where(entry => entry is not null)
                .Cast<WorkbenchCacheEntry>()
                .Select(entry =>
                {
                    entry.PendingSync = pendingIds.Contains(entry.Id);
                    if (previousById.TryGetValue(entry.Id, out var previous))
                    {
                        entry.DesktopCard = previous.DesktopCard;
                        entry.LastTriggeredAt = previous.LastTriggeredAt;
                    }
                    if (reminders.TryGetValue(entry.Id, out var reminder))
                    {
                        entry.ReminderAt = reminder.triggerAt;
                        entry.ReminderRepeat = reminder.repeat ?? "none";
                    }
                    else
                    {
                        entry.ReminderAt = null;
                        entry.ReminderRepeat = "none";
                    }
                    return entry;
                })
                .ToList();
            var serverIds = new HashSet<string>(merged.Select(entry => entry.Id));
            foreach (var local in locals.Where(entry => entry.PendingSync))
            {
                if (serverIds.Contains(local.Id))
                {
                    continue;
                }
                if (previousById.TryGetValue(local.Id, out var localPrevious))
                {
                    local.DesktopCard = localPrevious.DesktopCard;
                    local.LastTriggeredAt = localPrevious.LastTriggeredAt;
                }
                merged.Add(local);
            }
            _cache.Entries = merged;
        }
        await SaveCacheAsync();
        CacheChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>冲刷离线队列、重发未同步记录并拉取最新数据。</summary>
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (!_cacheLoaded)
        {
            await LoadCacheAsync();
        }
        var flushed = await _client.FlushOutboxAsync(cancellationToken);
        if (flushed < 0)
        {
            return;
        }
        List<WorkbenchCacheEntry> pending;
        lock (_gate)
        {
            pending = _cache.Entries.Where(entry => entry.PendingSync).ToList();
        }
        var recovered = false;
        foreach (var entry in pending)
        {
            if (await EnqueueOrSendAsync(
                _client.NextOperationId(),
                "create",
                entry.Id,
                EntryCommand(entry.Id),
                null,
                new { }))
            {
                entry.PendingSync = false;
                recovered = true;
            }
        }
        if (recovered)
        {
            await SaveCacheAsync();
        }
        await RefreshAsync(cancellationToken);
    }

    private static string? ToIso(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateTimeOffset? ParseInstant(string? iso)
        => ToIso(iso) is { } value
            ? DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)
            : null;

    private static string? DueFor(WorkbenchCacheEntry entry)
    {
        if (ToIso(entry.ReminderAt) is { } reminder)
        {
            return reminder;
        }
        if (ToIso(entry.DueAt) is { } due)
        {
            return due;
        }
        if (!string.IsNullOrWhiteSpace(entry.Date) && !string.IsNullOrWhiteSpace(entry.StartTime))
        {
            return DateTimeOffset.Parse(
                $"{entry.Date}T{entry.StartTime}:00",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind).ToString("O");
        }
        return null;
    }

    private static EntryStatus MapStatus(WorkbenchCacheEntry entry)
        => entry.Deleted
            ? EntryStatus.Archived
            : entry.Status == "done"
                ? EntryStatus.Completed
                : EntryStatus.Active;

    private static string SplitTitle(string content, out string notes)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var title = lines[0].Trim();
        if (title.Length > 160)
        {
            title = title[..160];
        }
        notes = string.Join('\n', lines.Skip(1)).Trim();
        return title;
    }

    private static DateTimeOffset? ComputeDue(WorkbenchCacheEntry entry)
        => ParseInstant(DueFor(entry));

    private static RepeatRule MapRepeat(string repeat)
        => repeat switch
        {
            "daily" => RepeatRule.Daily,
            "weekdays" => RepeatRule.Weekdays,
            "weekly" => RepeatRule.Weekly,
            _ => RepeatRule.None,
        };

    private EntryItem Present(WorkbenchCacheEntry entry)
    {
        var metadata = new Dictionary<string, string>();
        if (entry.PendingSync)
        {
            metadata["sync"] = "pending";
        }
        if (!string.IsNullOrWhiteSpace(entry.Notes))
        {
            metadata["notes"] = entry.Notes;
        }
        return new EntryItem(
            entry.Id,
            ProviderId,
            entry.Id,
            "workbench",
            metadata,
            entry.Revision,
            ParseInstant(entry.UpdatedAt) ?? DateTimeOffset.MinValue,
            ParseInstant(entry.UpdatedAt) ?? DateTimeOffset.MinValue,
            MapStatus(entry),
            entry.Title,
            entry.Tags,
            entry.Favorite,
            ComputeDue(entry),
            ParseInstant(entry.LastTriggeredAt),
            MapRepeat(entry.ReminderRepeat),
            entry.DesktopCard ?? new DesktopCardState());
    }

    private JsonElement EntryCommand(string id)
    {
        var entry = _cache.Entries.First(item => item.Id == id);
        return JsonSerializer.SerializeToElement(new
        {
            id = entry.Id,
            kind = entry.Kind,
            title = entry.Title,
            notes = entry.Notes,
            projectId = entry.ProjectId,
            date = entry.Date,
            startTime = entry.StartTime,
            endTime = (string?)null,
            status = entry.Status,
            dueAt = entry.DueAt,
            tags = entry.Tags,
            favorite = entry.Favorite,
            recurrence = entry.Recurrence,
        }, JsonOptions);
    }

    private async Task<bool> EnqueueOrSendAsync(
        string operationId,
        string action,
        string entityId,
        JsonElement? entry,
        long? expectedRevision,
        object? extra)
    {
        var payload = new Dictionary<string, object>
        {
            ["operationId"] = operationId,
            ["action"] = action,
        };
        if (!string.IsNullOrEmpty(entityId))
        {
            payload["entityId"] = entityId;
        }
        if (entry is not null)
        {
            payload["entry"] = entry.Value;
        }
        if (expectedRevision is { } revision)
        {
            payload["expectedRevision"] = revision;
        }
        if (extra is not null)
        {
            foreach (var property in System.Text.Json.JsonDocument
                .Parse(JsonSerializer.Serialize(extra, JsonOptions)).RootElement.EnumerateObject())
            {
                payload[property.Name] = property.Value.Clone();
            }
        }
        var command = JsonSerializer.SerializeToElement(payload, JsonOptions);
        try
        {
            await _client.SendAsync(new[] { command });
            return true;
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                or TaskCanceledException
                or UnauthorizedAccessException)
        {
            await _client.EnqueueAsync(command);
            return false;
        }
    }

    public async Task<EntryItem> CreateAsync(
        CreateEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        var title = SplitTitle(command.Content, out var notes);
        var entry = new WorkbenchCacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Kind = command.DueAt is null ? "note" : "task",
            Title = title,
            Notes = notes,
            Status = "todo",
            Date = command.DueAt?.ToString("yyyy-MM-dd"),
            StartTime = command.DueAt?.ToString("HH:mm"),
            DueAt = command.DueAt?.ToString("O"),
            Tags = command.Tags?.ToList() ?? new List<string>(),
            Favorite = false,
            Recurrence = "none",
            Revision = 1,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
            PendingSync = true,
        };
        lock (_gate)
        {
            _cache.Entries.Insert(0, entry);
        }
        await SaveCacheAsync();
        var delivered = await EnqueueOrSendAsync(
            _client.NextOperationId(),
            "create",
            entry.Id,
            EntryCommand(entry.Id),
            null,
            new { });
        if (delivered)
        {
            entry.PendingSync = false;
        }
        await SaveCacheAsync();
        CacheChanged?.Invoke(this, EventArgs.Empty);
        cancellationToken.ThrowIfCancellationRequested();
        return Present(entry);
    }

    public Task<EntryItem?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var entry = _cache.Entries.FirstOrDefault(item => item.Id == id && !item.Deleted);
            return Task.FromResult<EntryItem?>(entry is null ? null : Present(entry));
        }
    }

    public Task<IReadOnlyList<EntryItem>> QueryAsync(
        EntryQuery query,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IEnumerable<WorkbenchCacheEntry> entries = _cache.Entries;
            if (!query.IncludeArchived)
            {
                entries = entries.Where(item => !item.Deleted);
            }
            if (!query.IncludeCompleted)
            {
                entries = entries.Where(item => item.Status != "done");
            }
            if (!string.IsNullOrWhiteSpace(query.Text))
            {
                var text = query.Text.Trim();
                entries = entries.Where(item =>
                    item.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || item.Notes.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || item.Tags.Any(tag => tag.Contains(text, StringComparison.OrdinalIgnoreCase)));
            }
            if (query.PinnedToDesktop is { } pinned)
            {
                entries = entries.Where(item =>
                    (item.DesktopCard?.IsPinned ?? false) == pinned);
            }
            if (query.DueBefore is { } dueBefore)
            {
                entries = entries.Where(item => ComputeDue(item) is { } due && due <= dueBefore);
            }
            IReadOnlyList<EntryItem> result = entries
                .OrderByDescending(item => item.UpdatedAt)
                .Take(query.Limit)
                .Select(Present)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public async Task<EntryItem> UpdateAsync(
        string id,
        UpdateEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        WorkbenchCacheEntry entry;
        lock (_gate)
        {
            entry = _cache.Entries.First(item => item.Id == id);
        }
        var notes = entry.Notes;
        var title = command.Content is null
            ? entry.Title
            : SplitTitle(command.Content, out notes);
        entry.Title = title;
        entry.Notes = notes;
        if (command.Tags is not null)
        {
            entry.Tags = command.Tags.ToList();
        }
        if (command.IsFavorite is { } favorite)
        {
            entry.Favorite = favorite;
        }
        if (command.ClearDueAt)
        {
            entry.DueAt = null;
            entry.Date = null;
            entry.StartTime = null;
        }
        else if (command.DueAt is { } dueAt)
        {
            entry.DueAt = dueAt.ToString("O");
            entry.Date = dueAt.ToString("yyyy-MM-dd");
            entry.StartTime = dueAt.ToString("HH:mm");
        }
        entry.Revision += 1;
        entry.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await SaveCacheAsync();
        await EnqueueOrSendAsync(
            _client.NextOperationId(),
            "update",
            id,
            null,
            null,
            new { patch = EntryCommand(id) });
        CacheChanged?.Invoke(this, EventArgs.Empty);
        return Present(entry);
    }

    public async Task<EntryItem> SetStatusAsync(
        string id,
        EntryStatus status,
        CancellationToken cancellationToken = default)
        => status switch
        {
            EntryStatus.Completed => await CompleteAsync(id, cancellationToken),
            EntryStatus.Archived => await TrashAsync(id),
            _ => await UpdateStatusAsync(id, "doing"),
        };

    private async Task<EntryItem> UpdateStatusAsync(string id, string status)
    {
        WorkbenchCacheEntry entry;
        lock (_gate)
        {
            entry = _cache.Entries.First(item => item.Id == id);
        }
        entry.Status = status;
        entry.Revision += 1;
        entry.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await SaveCacheAsync();
        await EnqueueOrSendAsync(
            _client.NextOperationId(),
            "update",
            id,
            null,
            null,
            new { patch = EntryCommand(id) });
        CacheChanged?.Invoke(this, EventArgs.Empty);
        return Present(entry);
    }

    public async Task<EntryItem> CompleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        WorkbenchCacheEntry entry;
        lock (_gate)
        {
            entry = _cache.Entries.First(item => item.Id == id);
        }
        entry.Status = "done";
        entry.Revision += 1;
        entry.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await SaveCacheAsync();
        await EnqueueOrSendAsync(
            _client.NextOperationId(),
            "complete",
            id,
            null,
            null,
            new { });
        CacheChanged?.Invoke(this, EventArgs.Empty);
        return Present(entry);
    }

    public async Task<EntryItem> TrashAsync(string id)
    {
        WorkbenchCacheEntry entry;
        lock (_gate)
        {
            entry = _cache.Entries.First(item => item.Id == id);
        }
        entry.Deleted = true;
        entry.Revision += 1;
        entry.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await SaveCacheAsync();
        await EnqueueOrSendAsync(
            _client.NextOperationId(),
            "trash",
            id,
            null,
            null,
            new { });
        CacheChanged?.Invoke(this, EventArgs.Empty);
        return Present(entry);
    }

    public async Task<EntryItem> SnoozeAsync(
        string id,
        DateTimeOffset newDueAt,
        CancellationToken cancellationToken = default)
    {
        WorkbenchCacheEntry entry;
        lock (_gate)
        {
            entry = _cache.Entries.First(item => item.Id == id);
        }
        entry.ReminderAt = newDueAt.ToString("O");
        entry.LastTriggeredAt = newDueAt.ToString("O");
        await SaveCacheAsync();
        await EnqueueOrSendAsync(
            _client.NextOperationId(),
            "reminder.upsert",
            id,
            null,
            null,
            new { triggerAt = newDueAt.ToString("O"), repeat = entry.ReminderRepeat });
        CacheChanged?.Invoke(this, EventArgs.Empty);
        return Present(entry);
    }

    public async Task<EntryItem> SetDesktopCardAsync(
        string id,
        DesktopCardState state,
        CancellationToken cancellationToken = default)
    {
        WorkbenchCacheEntry entry;
        lock (_gate)
        {
            entry = _cache.Entries.First(item => item.Id == id);
        }
        entry.DesktopCard = state;
        await SaveCacheAsync();
        return Present(entry);
    }

    public Task<IReadOnlyList<EntryItem>> GetMissedAsync(
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var now = asOf ?? DateTimeOffset.Now;
        lock (_gate)
        {
            IReadOnlyList<EntryItem> missed = _cache.Entries
                .Where(entry => !entry.Deleted && entry.Status != "done")
                .Select(entry => new
                {
                    Entry = entry,
                    Due = ComputeDue(entry),
                })
                .Where(item => item.Due is { } due && due <= now)
                .Where(item =>
                {
                    var last = ParseInstant(item.Entry.LastTriggeredAt);
                    if (last is null)
                    {
                        return true;
                    }
                    return item.Entry.ReminderRepeat == "none"
                        ? false
                        : NextRepeat(item.Due!.Value, item.Entry.ReminderRepeat, last.Value) <= now;
                })
                .Select(item => Present(item.Entry))
                .ToList();
            return Task.FromResult(missed);
        }
    }

    private static DateTimeOffset NextRepeat(
        DateTimeOffset due,
        string repeat,
        DateTimeOffset after)
    {
        var cursor = due;
        int guard = 0;
        while (cursor <= after && guard < 1000)
        {
            cursor = repeat switch
            {
                "daily" => cursor.AddDays(1),
                "weekly" => cursor.AddDays(7),
                "weekdays" => cursor.AddDays(
                    cursor.DayOfWeek is DayOfWeek.Saturday ? 2
                    : cursor.DayOfWeek is DayOfWeek.Friday ? 3
                    : 1),
                _ => cursor,
            };
            guard += 1;
        }
        return cursor;
    }

    public Task<EntryItem?> TryMarkTriggeredIfDueAsync(
        string id,
        long expectedRevision,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var entry = _cache.Entries.FirstOrDefault(item => item.Id == id);
            if (entry is null || entry.Deleted || entry.Status == "done")
            {
                return Task.FromResult<EntryItem?>(null);
            }
            var due = ComputeDue(entry);
            if (due is null || due.Value > asOf)
            {
                return Task.FromResult<EntryItem?>(null);
            }
            if (entry.ReminderRepeat == "none"
                && entry.LastTriggeredAt is not null)
            {
                return Task.FromResult<EntryItem?>(null);
            }
            entry.LastTriggeredAt = asOf.ToString("O");
            entry.Revision += 1;
            return Task.FromResult<EntryItem?>(Present(entry));
        }
    }

    public async Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        WorkbenchCacheEntry? entry;
        lock (_gate)
        {
            entry = _cache.Entries.FirstOrDefault(item => item.Id == id);
        }
        if (entry is null)
        {
            return false;
        }
        entry.Deleted = true;
        entry.Revision += 1;
        entry.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await SaveCacheAsync();
        await EnqueueOrSendAsync(
            _client.NextOperationId(),
            "trash",
            id,
            null,
            null,
            new { });
        CacheChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}

using System.Text.Json.Serialization;

namespace Toolbox.Core;

[JsonConverter(typeof(JsonStringEnumConverter<FileShelfItemKind>))]
public enum FileShelfItemKind
{
    File,
    Directory,
}

public sealed class FileShelfDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumItemCount = 200;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<FileShelfBatch> Batches { get; set; } = [];

    [JsonIgnore]
    public int ItemCount => Batches.Sum(batch => batch.Items.Count);

    [JsonIgnore]
    public int PinnedCount => Batches.Sum(batch => batch.Items.Count(item => item.IsPinned));

    public FileShelfDocument Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Batches = Batches.Select(batch => batch.Clone()).ToList(),
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion is < 1 or > CurrentSchemaVersion)
        {
            errors.Add("Drop 数据版本不受支持。");
        }

        Batches ??= [];
        if (ItemCount > MaximumItemCount)
        {
            errors.Add($"Drop 最多保存 {MaximumItemCount} 个路径。");
        }

        var batchIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in Batches)
        {
            batch.Items ??= [];
            if (string.IsNullOrWhiteSpace(batch.Id) || !batchIds.Add(batch.Id))
            {
                errors.Add("Drop 包含无效或重复的批次标识。");
            }

            foreach (var item in batch.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || !itemIds.Add(item.Id))
                {
                    errors.Add("Drop 包含无效或重复的项目标识。");
                }

                if (string.IsNullOrWhiteSpace(item.Path) || !Path.IsPathFullyQualified(item.Path))
                {
                    errors.Add("Drop 只接受本地绝对路径。");
                }

                if (string.IsNullOrWhiteSpace(item.DisplayName))
                {
                    errors.Add("Drop 项目缺少显示名称。");
                }
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public void Normalize()
    {
        Batches ??= [];
        Batches = Batches
            .Where(batch => batch is not null)
            .OrderByDescending(batch => batch.AddedAt)
            .ToList();
        foreach (var batch in Batches)
        {
            batch.Items ??= [];
            batch.Items = batch.Items.OrderBy(item => item.Order).ToList();
        }
    }
}

public sealed class FileShelfBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    public List<FileShelfItem> Items { get; set; } = [];

    public FileShelfBatch Clone() => new()
    {
        Id = Id,
        AddedAt = AddedAt,
        Items = Items.Select(item => item.Clone()).ToList(),
    };
}

public sealed class FileShelfItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Path { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public FileShelfItemKind Kind { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    public int Order { get; set; }

    public bool IsPinned { get; set; }

    public FileShelfItem Clone() => new()
    {
        Id = Id,
        Path = Path,
        DisplayName = DisplayName,
        Kind = Kind,
        AddedAt = AddedAt,
        Order = Order,
        IsPinned = IsPinned,
    };
}

public static class FileShelfOperations
{
    public static FileShelfBatch AddBatch(FileShelfDocument document, IEnumerable<string> paths)
    {
        var accepted = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Take(Math.Max(0, FileShelfDocument.MaximumItemCount - document.ItemCount))
            .ToArray();
        if (accepted.Length == 0)
        {
            throw new InvalidOperationException("没有可加入 Drop 的本地文件或文件夹。");
        }

        var now = DateTimeOffset.Now;
        var batch = new FileShelfBatch
        {
            AddedAt = now,
            Items = accepted.Select((path, index) => new FileShelfItem
            {
                Path = path,
                DisplayName = GetDisplayName(path),
                Kind = Directory.Exists(path) ? FileShelfItemKind.Directory : FileShelfItemKind.File,
                AddedAt = now,
                Order = index,
            }).ToList(),
        };
        document.Batches.Insert(0, batch);
        return batch;
    }

    public static IReadOnlyList<FileShelfItem> RemoveItems(
        FileShelfDocument document,
        IEnumerable<string> itemIds,
        bool includePinned)
    {
        var wanted = itemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = new List<FileShelfItem>();
        foreach (var batch in document.Batches)
        {
            var matches = batch.Items
                .Where(item => wanted.Contains(item.Id) && (includePinned || !item.IsPinned))
                .ToArray();
            removed.AddRange(matches.Select(item => item.Clone()));
            batch.Items.RemoveAll(item => matches.Contains(item));
        }

        document.Batches.RemoveAll(batch => batch.Items.Count == 0);
        return removed;
    }

    public static IReadOnlyList<FileShelfItem> ClearUnpinned(FileShelfDocument document) =>
        RemoveItems(
            document,
            document.Batches.SelectMany(batch => batch.Items).Where(item => !item.IsPinned).Select(item => item.Id),
            includePinned: false);

    private static string GetDisplayName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }
}

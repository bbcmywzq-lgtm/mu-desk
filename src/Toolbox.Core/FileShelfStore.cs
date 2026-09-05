using System.Text.Json;

namespace Toolbox.Core;

public sealed record FileShelfLoadResult(
    FileShelfDocument Document,
    bool RecoveredFromBackup,
    string? ErrorMessage);

public sealed class FileShelfStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _documentPath;
    private readonly string _backupPath;

    public FileShelfStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MU Desk",
            "file-shelf",
            "shelf.json"))
    {
    }

    public FileShelfStore(string documentPath)
    {
        _documentPath = Path.GetFullPath(documentPath);
        _backupPath = _documentPath + ".bak";
    }

    public string DataFolder => Path.GetDirectoryName(_documentPath)!;

    public FileShelfLoadResult Load()
    {
        if (!File.Exists(_documentPath))
        {
            return new FileShelfLoadResult(new FileShelfDocument(), false, null);
        }

        if (TryLoadFile(_documentPath, out var document, out var primaryError))
        {
            return new FileShelfLoadResult(document!, false, null);
        }

        if (File.Exists(_backupPath) && TryLoadFile(_backupPath, out document, out _))
        {
            return new FileShelfLoadResult(
                document!,
                true,
                $"货架主数据无法读取，已从最近备份恢复：{primaryError}");
        }

        return new FileShelfLoadResult(
            new FileShelfDocument(),
            false,
            $"货架数据和备份均无法读取，原文件已保留：{primaryError}");
    }

    public bool TrySave(FileShelfDocument document, out string? errorMessage)
    {
        try
        {
            document.SchemaVersion = FileShelfDocument.CurrentSchemaVersion;
            document.Normalize();
            var errors = document.Validate();
            if (errors.Count > 0)
            {
                errorMessage = string.Join(" ", errors);
                return false;
            }

            var folder = Path.GetDirectoryName(_documentPath)!;
            Directory.CreateDirectory(folder);
            var temporaryPath = _documentPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            if (File.Exists(_documentPath))
            {
                File.Replace(temporaryPath, _documentPath, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _documentPath);
            }

            errorMessage = null;
            return true;
        }
        catch (IOException exception)
        {
            errorMessage = $"Drop 无法保存：{exception.Message}";
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            errorMessage = $"没有权限保存 Drop：{exception.Message}";
            return false;
        }
        catch (JsonException exception)
        {
            errorMessage = $"Drop 无法序列化：{exception.Message}";
            return false;
        }
    }

    private static bool TryLoadFile(string path, out FileShelfDocument? document, out string? errorMessage)
    {
        try
        {
            document = JsonSerializer.Deserialize<FileShelfDocument>(File.ReadAllText(path), JsonOptions);
            if (document is null)
            {
                errorMessage = "数据文件为空。";
                return false;
            }

            document.Normalize();
            var errors = document.Validate();
            if (errors.Count > 0)
            {
                errorMessage = string.Join(" ", errors);
                document = null;
                return false;
            }

            errorMessage = null;
            return true;
        }
        catch (IOException exception)
        {
            document = null;
            errorMessage = exception.Message;
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            document = null;
            errorMessage = exception.Message;
            return false;
        }
        catch (JsonException exception)
        {
            document = null;
            errorMessage = exception.Message;
            return false;
        }
    }
}

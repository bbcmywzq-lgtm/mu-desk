using System.IO;
using System.Text.Json;
using DesktopOrganizer.Core.Models;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Services;

public sealed class LayoutStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _layoutPath;
    private readonly string _backupPath;

    public LayoutStore(string? dataDirectory = null)
    {
        dataDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopOrganizer");
        _layoutPath = Path.Combine(dataDirectory, "layout.json");
        _backupPath = Path.Combine(dataDirectory, "layout.backup.json");
    }

    public LayoutSnapshot? Load()
    {
        var primary = TryLoad(_layoutPath);
        if (primary is not null)
        {
            return primary;
        }

        var backup = TryLoad(_backupPath);
        if (backup is null)
        {
            return null;
        }

        AppLog.Warning("Primary layout was unavailable; recovered the backup layout.");
        QuarantineInvalidPrimary();
        Save(backup);
        return backup;
    }

    public bool Save(LayoutSnapshot snapshot)
    {
        try
        {
            var directory = Path.GetDirectoryName(_layoutPath)!;
            Directory.CreateDirectory(directory);

            LayoutStateManager.Normalize(snapshot);
            snapshot.SavedAt = DateTimeOffset.UtcNow;

            var temporaryPath = _layoutPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, SerializerOptions));
            if (File.Exists(_layoutPath))
            {
                File.Copy(_layoutPath, _backupPath, true);
            }

            File.Move(temporaryPath, _layoutPath, true);
            return true;
        }
        catch (IOException exception)
        {
            AppLog.Error("Could not save the layout.", exception);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Layout storage is not writable.", exception);
            return false;
        }
    }

    private static LayoutSnapshot? Deserialize(string json)
    {
        var snapshot = JsonSerializer.Deserialize<LayoutSnapshot>(json, SerializerOptions);
        if (snapshot is null || snapshot.SchemaVersion is < 3 or > LayoutSnapshot.CurrentSchemaVersion)
        {
            return null;
        }

        LayoutStateManager.Normalize(snapshot);
        return snapshot;
    }

    private LayoutSnapshot? TryLoad(string path)
    {
        try
        {
            return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : null;
        }
        catch (JsonException exception)
        {
            AppLog.Error($"Layout file is not valid JSON: {Path.GetFileName(path)}", exception);
            return null;
        }
        catch (IOException exception)
        {
            AppLog.Error($"Could not read layout file: {Path.GetFileName(path)}", exception);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error($"Layout file is not readable: {Path.GetFileName(path)}", exception);
            return null;
        }
    }

    private void QuarantineInvalidPrimary()
    {
        try
        {
            if (!File.Exists(_layoutPath))
            {
                return;
            }

            var quarantinePath = Path.Combine(
                Path.GetDirectoryName(_layoutPath)!,
                $"layout.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
            File.Move(_layoutPath, quarantinePath, true);
        }
        catch (IOException exception)
        {
            AppLog.Error("Could not quarantine the invalid layout.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Could not quarantine the invalid layout.", exception);
        }
    }
}

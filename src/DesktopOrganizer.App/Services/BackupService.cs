using System.IO;
using System.Text.Json;
using DesktopOrganizer.Core.Models;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Services;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _recoveryDirectory;

    public BackupService(string? dataDirectory = null)
    {
        dataDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopOrganizer");
        _recoveryDirectory = Path.Combine(dataDirectory, "recovery");
    }

    public bool Export(string path, LayoutSnapshot layout, AppSettings settings)
    {
        try
        {
            var bundle = CreateBundle(layout, settings);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(bundle, SerializerOptions));
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (IOException exception)
        {
            AppLog.Error("Could not export a backup.", exception);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Backup destination is not writable.", exception);
            return false;
        }
    }

    public BackupBundle? Import(string path)
    {
        try
        {
            var bundle = JsonSerializer.Deserialize<BackupBundle>(File.ReadAllText(path), SerializerOptions);
            if (bundle is null ||
                bundle.FormatVersion != BackupBundle.CurrentFormatVersion ||
                bundle.Settings.SchemaVersion is < 1 or > AppSettings.CurrentSchemaVersion)
            {
                return null;
            }

            bundle.Settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            LayoutStateManager.Normalize(bundle.Layout);
            return bundle;
        }
        catch (JsonException exception)
        {
            AppLog.Error("Backup file is not valid JSON.", exception);
            return null;
        }
        catch (IOException exception)
        {
            AppLog.Error("Could not read the backup file.", exception);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Backup file is not readable.", exception);
            return null;
        }
    }

    public bool CreateRecoveryPoint(LayoutSnapshot layout, AppSettings settings)
    {
        var path = Path.Combine(
            _recoveryDirectory,
            $"before-import-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.desktoporganizer");
        return Export(path, layout, settings);
    }

    private static BackupBundle CreateBundle(LayoutSnapshot layout, AppSettings settings)
    {
        var layoutClone = layout.Clone();
        LayoutStateManager.Normalize(layoutClone);
        return new BackupBundle
        {
            CreatedAt = DateTimeOffset.UtcNow,
            Layout = layoutClone,
            Settings = settings.Clone(),
        };
    }
}

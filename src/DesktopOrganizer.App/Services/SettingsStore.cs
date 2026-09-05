using System.IO;
using System.Text.Json;
using DesktopOrganizer.Core.Models;

namespace DesktopOrganizer.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public SettingsStore(string? dataDirectory = null)
    {
        dataDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopOrganizer");
        _settingsPath = Path.Combine(dataDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath));
            if (settings is null || settings.SchemaVersion is < 1 or > AppSettings.CurrentSchemaVersion)
            {
                return new AppSettings();
            }

            settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            return settings;
        }
        catch (JsonException exception)
        {
            AppLog.Error("Settings file is not valid JSON.", exception);
            return new AppSettings();
        }
        catch (IOException exception)
        {
            AppLog.Error("Could not read settings.", exception);
            return new AppSettings();
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Settings file is not readable.", exception);
            return new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, _settingsPath, true);
            return true;
        }
        catch (IOException exception)
        {
            AppLog.Error("Could not save settings.", exception);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Settings storage is not writable.", exception);
            return false;
        }
    }
}

using System.IO;
using System.Text.Json;
using Toolbox.Core;

namespace PersonalToolbox.Services;

public sealed class ToolboxSettingsStore
{
    private readonly string _settingsPath;

    public ToolboxSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MU Desk"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalToolbox"))
    {
    }

    internal ToolboxSettingsStore(string targetFolder, string legacyFolder)
    {
        var migration = ToolboxSettingsMigration.Establish(
            Path.Combine(targetFolder, "settings.json"),
            Path.Combine(legacyFolder, "settings.json"));
        _settingsPath = migration.SettingsPath;
        LastError = migration.ErrorMessage;
    }

    public string? LastError { get; private set; }

    public ToolboxSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return CreateDefaults();
            }

            var settings = JsonSerializer.Deserialize<ToolboxSettings>(File.ReadAllText(_settingsPath)) ??
                CreateDefaults();
            settings.Normalize();
            return settings;
        }
        catch (IOException exception)
        {
            LastError = $"工具箱设置读取失败：{exception.Message}";
            return CreateDefaults();
        }
        catch (JsonException exception)
        {
            LastError = $"工具箱设置格式无效：{exception.Message}";
            return CreateDefaults();
        }
        catch (UnauthorizedAccessException exception)
        {
            LastError = $"没有权限读取工具箱设置：{exception.Message}";
            return CreateDefaults();
        }
    }

    public bool Save(ToolboxSettings settings)
    {
        try
        {
            settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
            LastError = null;
            return true;
        }
        catch (IOException exception)
        {
            LastError = $"工具箱设置写入失败：{exception.Message}";
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            LastError = $"没有权限写入工具箱设置：{exception.Message}";
            return false;
        }
    }

    private static ToolboxSettings CreateDefaults()
    {
        var settings = new ToolboxSettings();
        settings.Normalize();
        return settings;
    }
}

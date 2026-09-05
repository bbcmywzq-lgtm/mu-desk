namespace Toolbox.Core;

public sealed record ToolboxSettingsMigrationResult(
    string SettingsPath,
    bool TargetEstablished,
    bool Migrated,
    bool UsingLegacyFallback,
    string? ErrorMessage);

public static class ToolboxSettingsMigration
{
    public static ToolboxSettingsMigrationResult Establish(string targetPath, string legacyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);

        targetPath = Path.GetFullPath(targetPath);
        legacyPath = Path.GetFullPath(legacyPath);
        if (File.Exists(targetPath))
        {
            return new(targetPath, true, false, false, null);
        }

        if (!File.Exists(legacyPath))
        {
            return new(targetPath, false, false, false, null);
        }

        var temporaryPath = targetPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(legacyPath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, targetPath, overwrite: false);
            return new(targetPath, true, true, false, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            return new(
                legacyPath,
                false,
                false,
                true,
                $"工具箱设置迁移失败：{exception.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

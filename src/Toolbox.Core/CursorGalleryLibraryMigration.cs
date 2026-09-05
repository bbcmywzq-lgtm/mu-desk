using System;
using System.IO;

namespace Toolbox.Core;

public sealed record CursorGalleryLibraryLocation(
    string LibraryPath,
    bool TargetEstablished,
    bool Migrated,
    bool UsingLegacyFallback,
    string? ErrorMessage);

public static class CursorGalleryLibraryMigration
{
    public static CursorGalleryLibraryLocation Establish(string targetPath, string legacyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);

        targetPath = Path.GetFullPath(targetPath);
        legacyPath = Path.GetFullPath(legacyPath);

        if (File.Exists(targetPath))
        {
            return new CursorGalleryLibraryLocation(targetPath, true, false, false, null);
        }

        if (!File.Exists(legacyPath))
        {
            return new CursorGalleryLibraryLocation(targetPath, false, false, false, null);
        }

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Tip 目标库路径无效。");
        var temporaryPath = targetPath + $".migrating-{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(targetDirectory);
            File.Copy(legacyPath, temporaryPath, overwrite: false);
            try
            {
                File.Move(temporaryPath, targetPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                File.Delete(temporaryPath);
                return new CursorGalleryLibraryLocation(targetPath, true, false, false, null);
            }

            return new CursorGalleryLibraryLocation(targetPath, true, true, false, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            return new CursorGalleryLibraryLocation(
                legacyPath,
                false,
                false,
                true,
                $"无法把旧光标库复制到 MU Desk：{exception.Message}");
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

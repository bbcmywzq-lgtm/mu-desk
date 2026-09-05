using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopOrganizer.Core.Models;
using DesktopOrganizer.Models;

namespace DesktopOrganizer.Services;

public sealed class SmartFolderCatalog
{
    private const int LargeFolderThreshold = 10000;
    private static readonly HashSet<string> ThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff",
    };

    public Task<SmartFolderScanResult> ScanAsync(
        ZoneLayout zone,
        string? currentPath = null,
        IProgress<IReadOnlyList<DesktopEntry>>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(zone, currentPath, progress, cancellationToken), cancellationToken);

    public static string ResolveNavigationPath(ZoneLayout zone, string? candidatePath)
    {
        if (zone.Kind != ZoneKind.SmartFolder || string.IsNullOrWhiteSpace(zone.SourcePath))
        {
            return string.Empty;
        }

        try
        {
            var rootPath = Path.GetFullPath(zone.SourcePath);
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return rootPath;
            }

            var fullCandidate = Path.GetFullPath(candidatePath);
            return Directory.Exists(fullCandidate) && IsPathWithinRoot(rootPath, fullCandidate)
                ? fullCandidate
                : rootPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return zone.SourcePath;
        }
    }

    public static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(candidatePath));
            return relative == "." ||
                   (relative != ".." &&
                    !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public static bool MatchesFilter(ZoneLayout zone, string path)
    {
        if (zone.Kind != ZoneKind.SmartFolder || Directory.Exists(path))
        {
            return false;
        }

        return zone.Extensions.Count == 0 || zone.Extensions.Contains(
            Path.GetExtension(path),
            StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsLocalFixedPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return !string.IsNullOrWhiteSpace(root) && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static SmartFolderScanResult Scan(
        ZoneLayout zone,
        string? currentPath,
        IProgress<IReadOnlyList<DesktopEntry>>? progress,
        CancellationToken cancellationToken)
    {
        if (zone.Kind != ZoneKind.SmartFolder || string.IsNullOrWhiteSpace(zone.SourcePath))
        {
            return new SmartFolderScanResult([], "尚未选择来源文件夹", false);
        }

        if (!Directory.Exists(zone.SourcePath))
        {
            return new SmartFolderScanResult([], "来源文件夹不可用", false);
        }

        try
        {
            var rootPath = Path.GetFullPath(zone.SourcePath);
            var browsePath = ResolveNavigationPath(zone, currentPath);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = zone.IncludeSubfolders,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = zone.ShowHiddenFiles
                    ? 0
                    : FileAttributes.Hidden | FileAttributes.System,
            };
            var entries = new List<DesktopEntry>();
            var directoryOptions = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = options.AttributesToSkip,
            };
            foreach (var path in Directory.EnumerateDirectories(browsePath, "*", directoryOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new DirectoryInfo(path);
                    entries.Add(new DesktopEntry
                    {
                        Name = info.Name,
                        Path = info.FullName,
                        RelativeDirectory = "文件夹",
                        LastWriteTime = info.LastWriteTime,
                        IsDirectory = true,
                        Icon = ShellIconProvider.GetSmallIcon(info.FullName),
                    });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
            }

            foreach (var path in Directory.EnumerateFiles(browsePath, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesFilter(zone, path))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(path);
                    var relativeDirectory = Path.GetRelativePath(
                        rootPath,
                        info.DirectoryName ?? rootPath);
                    entries.Add(new DesktopEntry
                    {
                        Name = info.Name,
                        Path = info.FullName,
                        RelativeDirectory = relativeDirectory == "." ? string.Empty : relativeDirectory,
                        LastWriteTime = info.LastWriteTime,
                        Size = info.Length,
                        Icon = GetVisual(info.FullName),
                    });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (entries.Count % 200 == 0)
                {
                    progress?.Report(Sort(entries, zone.SortKind));
                }
            }

            var sorted = Sort(entries, zone.SortKind);
            progress?.Report(sorted);
            return new SmartFolderScanResult(sorted, null, sorted.Count >= LargeFolderThreshold);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SmartFolderScanResult([], exception.Message, false);
        }
    }

    private static IReadOnlyList<DesktopEntry> Sort(
        IEnumerable<DesktopEntry> entries,
        ZoneSortKind sortKind) => sortKind switch
        {
            ZoneSortKind.NameAscending => entries
                .OrderBy(entry => entry.IsDirectory ? 0 : 1)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ZoneSortKind.SizeDescending => entries
                .OrderBy(entry => entry.IsDirectory ? 0 : 1)
                .ThenByDescending(entry => entry.Size)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            _ => entries
                .OrderBy(entry => entry.IsDirectory ? 0 : 1)
                .ThenByDescending(entry => entry.LastWriteTime)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
        };

    private static ImageSource? GetVisual(string path)
    {
        if (!ThumbnailExtensions.Contains(Path.GetExtension(path)))
        {
            return ShellIconProvider.GetSmallIcon(path);
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 64;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return ShellIconProvider.GetSmallIcon(path);
        }
    }
}

public sealed record SmartFolderScanResult(
    IReadOnlyList<DesktopEntry> Entries,
    string? Error,
    bool IsLarge);

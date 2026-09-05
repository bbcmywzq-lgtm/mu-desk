using System.IO;
using DesktopOrganizer.Models;

namespace DesktopOrganizer.Services;

public sealed class DesktopCatalog
{
    public IReadOnlyList<string> GetDesktopDirectories()
    {
        return
        [
            .. new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            }
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    public IReadOnlyList<DesktopEntry> LoadEntries()
    {
        var entries = new List<DesktopEntry>();
        foreach (var directory in GetDesktopDirectories())
        {
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        continue;
                    }

                    entries.Add(new DesktopEntry
                    {
                        Name = Path.GetFileName(path),
                        Path = path,
                        Icon = ShellIconProvider.GetSmallIcon(path),
                    });
                }
            }
            catch (IOException)
            {
                // A redirected or synchronizing desktop may be briefly unavailable.
            }
            catch (UnauthorizedAccessException)
            {
                // Public desktop permissions can differ; keep the user desktop usable.
            }
        }

        return entries
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}

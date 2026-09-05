using System.IO;
using DesktopOrganizer.Core.Models;

namespace DesktopOrganizer.Services;

public sealed class FolderChangeMonitor : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];

    public event EventHandler? Changed;

    public void Start(IEnumerable<ZoneLayout> zones)
    {
        DisposeWatchers();
        var sources = zones
            .Where(zone => zone.Kind == ZoneKind.SmartFolder && Directory.Exists(zone.SourcePath))
            .GroupBy(zone => zone.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key);

        foreach (var source in sources)
        {
            try
            {
                var watcher = new FileSystemWatcher(source)
                {
                    // Navigation can enter any descendant even when the file-result
                    // filter itself is not recursive, so monitor the whole root.
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Renamed += OnChanged;
                _watchers.Add(watcher);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The zone remains visible as unavailable; other sources keep working.
            }
        }
    }

    public void Dispose()
    {
        DisposeWatchers();
        GC.SuppressFinalize(this);
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Changed -= OnChanged;
            watcher.Renamed -= OnChanged;
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}

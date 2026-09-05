using System.IO;

namespace DesktopOrganizer.Services;

public sealed class DesktopChangeMonitor : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];

    public event EventHandler? Changed;

    public event EventHandler<DesktopPathRenamedEventArgs>? Renamed;

    public void Start(IEnumerable<string> directories)
    {
        DisposeWatchers();

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true,
                };
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Renamed += OnRenamed;
                _watchers.Add(watcher);
            }
            catch (IOException)
            {
                // Redirected desktop folders can be temporarily unavailable.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep monitoring any other desktop directory that is accessible.
            }
        }
    }

    public void Dispose()
    {
        DisposeWatchers();
        GC.SuppressFinalize(this);
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Renamed?.Invoke(this, new DesktopPathRenamedEventArgs(e.OldFullPath, e.FullPath));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Changed -= OnChanged;
            watcher.Renamed -= OnRenamed;
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}

public sealed class DesktopPathRenamedEventArgs(string oldPath, string newPath) : EventArgs
{
    public string OldPath { get; } = oldPath;

    public string NewPath { get; } = newPath;
}

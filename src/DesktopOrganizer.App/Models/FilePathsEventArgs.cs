namespace DesktopOrganizer.Models;

public sealed class FilePathsEventArgs(IReadOnlyList<string> paths) : EventArgs
{
    public IReadOnlyList<string> Paths { get; } = paths;
}

namespace DesktopOrganizer.Models;

public sealed class ItemPathEventArgs(string path) : EventArgs
{
    public string Path { get; } = path;
}

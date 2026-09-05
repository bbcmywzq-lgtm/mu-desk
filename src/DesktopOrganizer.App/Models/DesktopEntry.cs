using System.Windows.Media;

namespace DesktopOrganizer.Models;

public sealed class DesktopEntry
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public ImageSource? Icon { get; init; }

    public string RelativeDirectory { get; init; } = string.Empty;

    public DateTime LastWriteTime { get; init; }

    public long Size { get; init; }

    public bool IsDirectory { get; init; }

    public string ModifiedText => LastWriteTime == default
        ? string.Empty
        : LastWriteTime.ToString("yyyy-MM-dd HH:mm");

    public string SizeText => IsDirectory ? string.Empty : Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024d:0.#} KB",
        < 1024L * 1024 * 1024 => $"{Size / (1024d * 1024):0.#} MB",
        _ => $"{Size / (1024d * 1024 * 1024):0.#} GB",
    };

    public override string ToString() => Name;
}

namespace DesktopOrganizer.Core.Models;

public sealed class BackupBundle
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public LayoutSnapshot Layout { get; set; } = new();

    public AppSettings Settings { get; set; } = new();
}

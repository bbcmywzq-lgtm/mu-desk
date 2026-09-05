namespace DesktopOrganizer.Core.Models;

public sealed class ZoneLayout
{
    public const double CollapsedWidth = 240;

    public const double CollapsedHeight = 44;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "收件箱";

    public double X { get; set; } = 72;

    public double Y { get; set; } = 72;

    public double Width { get; set; } = 560;

    public double Height { get; set; } = 390;

    public bool IsCollapsed { get; set; }

    public bool IsPinned { get; set; }

    public bool IsInbox { get; set; }

    public ZoneKind Kind { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public bool IncludeSubfolders { get; set; }

    public bool ShowHiddenFiles { get; set; }

    public List<string> Extensions { get; set; } = [];

    public ZoneSortKind SortKind { get; set; } = ZoneSortKind.ModifiedDescending;

    public ZoneDisplayMode DisplayMode { get; set; }

    public ZoneLayout Clone() => new()
    {
        Id = Id,
        Name = Name,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        IsCollapsed = IsCollapsed,
        IsPinned = IsPinned,
        IsInbox = IsInbox,
        Kind = Kind,
        SourcePath = SourcePath,
        IncludeSubfolders = IncludeSubfolders,
        ShowHiddenFiles = ShowHiddenFiles,
        Extensions = Extensions.ToList(),
        SortKind = SortKind,
        DisplayMode = DisplayMode,
    };

    public void ClampTo(double availableWidth, double availableHeight)
    {
        const double minimumWidth = 360;
        const double minimumHeight = 260;
        Width = Math.Clamp(Width, minimumWidth, Math.Max(minimumWidth, availableWidth));
        Height = Math.Clamp(Height, minimumHeight, Math.Max(minimumHeight, availableHeight));

        var visibleWidth = IsCollapsed ? CollapsedWidth : Width;
        var visibleHeight = IsCollapsed ? CollapsedHeight : Height;
        X = Math.Clamp(X, 0, Math.Max(0, availableWidth - visibleWidth));
        Y = Math.Clamp(Y, 0, Math.Max(0, availableHeight - visibleHeight));
    }
}

using DesktopOrganizer.Core.Models;

namespace DesktopOrganizer.Models;

public sealed class ZoneLayoutChangedEventArgs(ZoneLayout layout) : EventArgs
{
    public ZoneLayout Layout { get; } = layout;
}


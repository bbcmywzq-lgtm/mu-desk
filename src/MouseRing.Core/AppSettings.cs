namespace MouseRing.Core;

public sealed class AppSettings
{
    public int VisualRevision { get; set; }

    public int TriggerDelayMs { get; set; } = 180;

    public double MenuDiameter { get; set; } = 260;

    public bool DisableInFullScreen { get; set; } = true;

    public bool RunAtStartup { get; set; } = true;

    public ActionKind UpAction { get; set; } = ActionKind.BringCodex;

    public ActionKind RightAction { get; set; } = ActionKind.RegionScreenshot;

    public ActionKind DownAction { get; set; } = ActionKind.ShowDesktop;

    public ActionKind LeftAction { get; set; } = ActionKind.ClipboardHistory;

    public ActionKind GetAction(Direction direction) => direction switch
    {
        Direction.Up => UpAction,
        Direction.Right => RightAction,
        Direction.Down => DownAction,
        Direction.Left => LeftAction,
        _ => ActionKind.None,
    };

    public void Normalize()
    {
        if (VisualRevision < 2)
        {
            if (Math.Abs(MenuDiameter - 240) < 0.1)
            {
                MenuDiameter = 260;
            }

            VisualRevision = 2;
        }

        TriggerDelayMs = Math.Clamp(TriggerDelayMs, 80, 800);
        MenuDiameter = Math.Clamp(MenuDiameter, 180, 360);
        UpAction = NormalizeAction(UpAction);
        RightAction = NormalizeAction(RightAction);
        DownAction = NormalizeAction(DownAction);
        LeftAction = NormalizeAction(LeftAction);
    }

    private static ActionKind NormalizeAction(ActionKind action) =>
        Enum.IsDefined(action) ? action : ActionKind.None;
}

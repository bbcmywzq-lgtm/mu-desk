using MouseRing.Core;

namespace MouseRing.Services;

public sealed record ActionOption(ActionKind Kind, string Label, string Glyph);

public static class ActionCatalog
{
    public static IReadOnlyList<ActionOption> Options { get; } =
    [
        new(ActionKind.None, "未设置", "+"),
        new(ActionKind.BringCodex, "Codex", "AI"),
        new(ActionKind.RegionScreenshot, "截图", "\uE722"),
        new(ActionKind.ShowDesktop, "桌面", "\uE7C4"),
        new(ActionKind.ClipboardHistory, "剪贴板", "\uE77F"),
        new(ActionKind.TaskView, "任务视图", "\uE7C4"),
        new(ActionKind.WindowsSearch, "搜索", "\uE721"),
        new(ActionKind.ToggleMute, "静音", "\uE74F"),
        new(ActionKind.OpenFileExplorer, "资源管理器", "\uEC50"),
        new(ActionKind.PreviousWindow, "上一窗口", "\uE8A7"),
        new(ActionKind.EffectCapture, "Clip", "REC"),
    ];

    public static IReadOnlyList<ActionOption> OptionsFor(bool isHosted) =>
        isHosted ? Options : Options.Where(option => option.Kind != ActionKind.EffectCapture).ToArray();

    public static ActionOption Get(ActionKind kind) =>
        Options.FirstOrDefault(option => option.Kind == kind) ?? Options[0];
}

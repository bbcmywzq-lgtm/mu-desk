namespace DesktopOrganizer.Core.Models;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool AutoApplyRules { get; set; } = true;

    public bool ShowCommandToolbar { get; set; } = true;

    public bool LaunchAtStartup { get; set; }

    public bool HideNativeDesktopIcons { get; set; }

    public bool HasConfirmedRealFileMoves { get; set; }

    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        AutoApplyRules = AutoApplyRules,
        ShowCommandToolbar = ShowCommandToolbar,
        LaunchAtStartup = LaunchAtStartup,
        HideNativeDesktopIcons = HideNativeDesktopIcons,
        HasConfirmedRealFileMoves = HasConfirmedRealFileMoves,
    };
}

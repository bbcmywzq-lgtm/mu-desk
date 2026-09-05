namespace Toolbox.Core;

public sealed class EffectCaptureSettings
{
    public const string DefaultGlobalHotkey = "Ctrl+Alt+R";

    public int DefaultDurationSeconds { get; set; } = 2;

    public bool CountdownEnabled { get; set; } = true;

    public string? OutputRoot { get; set; }

    public string? GlobalHotkey { get; set; } = DefaultGlobalHotkey;

    public bool? GlobalHotkeyConfigured { get; set; }

    public void Normalize()
    {
        if (DefaultDurationSeconds is not (2 or 5 or 7))
        {
            DefaultDurationSeconds = 2;
        }

        OutputRoot = NormalizeOptional(OutputRoot);
        GlobalHotkey = NormalizeOptional(GlobalHotkey);
        if (GlobalHotkeyConfigured is null)
        {
            GlobalHotkey ??= DefaultGlobalHotkey;
            GlobalHotkeyConfigured = true;
        }

        if (GlobalHotkey is not ("Ctrl+Alt+R" or "Ctrl+Shift+R"))
        {
            GlobalHotkey = null;
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

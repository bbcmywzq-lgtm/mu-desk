namespace Toolbox.Core;

public sealed class ToolboxSettings
{
    public bool RunAtStartup { get; set; } = true;

    public bool MouseRingEnabled { get; set; } = true;

    public bool DesktopOrganizerEnabled { get; set; } = true;

    public bool LightPetEnabled { get; set; } = true;

    public bool StartMinimized { get; set; }

    public EffectCaptureSettings EffectCapture { get; set; } = new();

    public FileShelfSettings FileShelf { get; set; } = new();

    public CueSettings Cue { get; set; } = new();

    public void Normalize()
    {
        EffectCapture ??= new EffectCaptureSettings();
        EffectCapture.Normalize();
        FileShelf ??= new FileShelfSettings();
        FileShelf.Normalize();
        Cue ??= new CueSettings();
        Cue.Normalize();
    }
}

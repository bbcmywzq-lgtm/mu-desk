using MouseRing.Core;

namespace MouseRing.Services;

public sealed class ActionExecutor : IDisposable
{
    private readonly CodexWindowService _codex = new();
    private readonly Action? _effectCapture;

    public ActionExecutor(Action? effectCapture = null)
    {
        _effectCapture = effectCapture;
    }

    public bool Execute(ActionKind action)
    {
        try
        {
            switch (action)
            {
                case ActionKind.None:
                    return true;
                case ActionKind.BringCodex:
                    return _codex.BringToForegroundOrLaunch();
                case ActionKind.RegionScreenshot:
                    InputSender.SendChord(InputSender.VkLWin, InputSender.VkShift, InputSender.VkS);
                    return true;
                case ActionKind.ShowDesktop:
                    InputSender.SendChord(InputSender.VkLWin, InputSender.VkD);
                    return true;
                case ActionKind.ClipboardHistory:
                    InputSender.SendChord(InputSender.VkLWin, InputSender.VkV);
                    return true;
                case ActionKind.TaskView:
                    InputSender.SendChord(InputSender.VkLWin, InputSender.VkTab);
                    return true;
                case ActionKind.WindowsSearch:
                    InputSender.SendChord(InputSender.VkLWin, InputSender.VkS);
                    return true;
                case ActionKind.ToggleMute:
                    InputSender.SendChord(InputSender.VkVolumeMute);
                    return true;
                case ActionKind.OpenFileExplorer:
                    InputSender.SendChord(InputSender.VkLWin, InputSender.VkE);
                    return true;
                case ActionKind.PreviousWindow:
                    InputSender.SendChord(InputSender.VkMenu, InputSender.VkTab);
                    return true;
                case ActionKind.EffectCapture:
                    if (_effectCapture is null)
                    {
                        return false;
                    }

                    _effectCapture();
                    return true;
                default:
                    return false;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose() => _codex.Dispose();
}

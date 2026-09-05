using System.Diagnostics;
using Toolbox.Core;

namespace PersonalToolbox.Services;

public sealed class CodexDeepLinkLauncher
{
    public Task OpenEffectAsync(EffectCapturePackageResult package, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var prompt = "请分析当前工作区里的这段短动效素材。先读取 effect.md，查看 contact-sheet.png 和 frames 文件夹中的关键帧；需要时再检查 source.mp4。请说明时间线、视觉变化、缓动、层级、颜色和实现要点，并给出可执行的 HTML/CSS/JavaScript 复现方案。不要修改这些素材文件。";
        var deepLink = $"codex://new?path={Uri.EscapeDataString(package.RootPath)}&prompt={Uri.EscapeDataString(prompt)}";

        try
        {
            Process.Start(new ProcessStartInfo(deepLink) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("无法打开 Codex。请确认 Codex 桌面应用已经安装并可正常启动。", exception);
        }

        return Task.CompletedTask;
    }
}

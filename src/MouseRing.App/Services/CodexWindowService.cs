using System.Diagnostics;
using MouseRing.Interop;

namespace MouseRing.Services;

public sealed class CodexWindowService : IDisposable
{
    private const string CodexAppsFolderTarget = @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App";

    private readonly NativeMethods.WinEventDelegate _foregroundCallback;
    private readonly nint _foregroundHook;
    private nint _lastCodexWindow;

    public CodexWindowService()
    {
        _foregroundCallback = OnForegroundChanged;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            nint.Zero,
            _foregroundCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
    }

    public bool BringToForegroundOrLaunch()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (IsCodexWindow(foreground))
        {
            _lastCodexWindow = foreground;
            return true;
        }

        var target = IsUsableWindow(_lastCodexWindow) ? _lastCodexWindow : FindCodexWindow();
        if (target != nint.Zero)
        {
            ActivateWindow(target);
            _lastCodexWindow = target;
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = CodexAppsFolderTarget,
                UseShellExecute = true,
            });
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_foregroundHook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
        }
    }

    private void OnForegroundChanged(
        nint hook,
        uint eventType,
        nint window,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (window != nint.Zero && IsCodexWindow(window))
        {
            _lastCodexWindow = window;
        }
    }

    private static nint FindCodexWindow()
    {
        var candidates = new List<(nint Window, long Area)>();
        NativeMethods.EnumWindows(
            (window, _) =>
            {
                if (!IsCodexWindow(window) || !NativeMethods.GetWindowRect(window, out var rect))
                {
                    return true;
                }

                var width = Math.Max(0, rect.Right - rect.Left);
                var height = Math.Max(0, rect.Bottom - rect.Top);
                candidates.Add((window, (long)width * height));
                return true;
            },
            nint.Zero);

        return candidates.OrderByDescending(candidate => candidate.Area).FirstOrDefault().Window;
    }

    private static bool IsUsableWindow(nint window) =>
        window != nint.Zero && NativeMethods.IsWindowVisible(window) && IsCodexWindow(window);

    private static bool IsCodexWindow(nint window)
    {
        if (window == nint.Zero || !NativeMethods.IsWindowVisible(window) ||
            NativeMethods.GetWindow(window, NativeMethods.GwOwner) != nint.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void ActivateWindow(nint window)
    {
        if (NativeMethods.IsIconic(window))
        {
            NativeMethods.ShowWindowAsync(window, NativeMethods.SwRestore);
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var currentThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(window, out _);

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            }

            if (targetThread != 0 && targetThread != currentThread)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            }

            NativeMethods.BringWindowToTop(window);
            NativeMethods.SetForegroundWindow(window);
        }
        finally
        {
            if (targetThread != 0 && targetThread != currentThread)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }

            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}

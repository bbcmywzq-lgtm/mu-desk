using System.IO;
using System.Runtime.InteropServices;

namespace DesktopOrganizer.Services;

public sealed class DesktopIconVisibilityService
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    private IntPtr _shellView;
    private bool _originallyVisible;
    private bool _stateCaptured;
    private readonly string _recoveryMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopOrganizer",
        "native-icons-hidden.lock");

    public void RecoverAfterUnexpectedExit()
    {
        if (!File.Exists(_recoveryMarkerPath))
        {
            return;
        }

        var shellView = FindShellView();
        if (shellView != IntPtr.Zero)
        {
            ShowWindow(shellView, SwShow);
            AppLog.Warning("Recovered native desktop icons after an unexpected previous exit.");
        }

        DeleteRecoveryMarker();
    }

    public bool Hide()
    {
        var shellView = FindShellView();
        if (shellView == IntPtr.Zero)
        {
            return false;
        }

        if (!_stateCaptured || _shellView != shellView)
        {
            _shellView = shellView;
            _originallyVisible = IsWindowVisible(shellView);
            _stateCaptured = true;
            if (_originallyVisible)
            {
                WriteRecoveryMarker();
            }
        }

        ShowWindow(shellView, SwHide);
        return true;
    }

    public void EnsureHidden()
    {
        if (_shellView == IntPtr.Zero || !IsWindow(_shellView) || IsWindowVisible(_shellView))
        {
            Hide();
        }
    }

    public void Restore()
    {
        if (!_stateCaptured)
        {
            return;
        }

        var shellView = _shellView != IntPtr.Zero && IsWindow(_shellView)
            ? _shellView
            : FindShellView();
        if (shellView != IntPtr.Zero && _originallyVisible)
        {
            ShowWindow(shellView, SwShow);
        }

        DeleteRecoveryMarker();
        _shellView = IntPtr.Zero;
        _stateCaptured = false;
    }

    private void WriteRecoveryMarker()
    {
        try
        {
            var directory = Path.GetDirectoryName(_recoveryMarkerPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_recoveryMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (IOException exception)
        {
            AppLog.Error("Could not write the native-icon recovery marker.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Could not write the native-icon recovery marker.", exception);
        }
    }

    private void DeleteRecoveryMarker()
    {
        try
        {
            File.Delete(_recoveryMarkerPath);
        }
        catch (IOException exception)
        {
            AppLog.Error("Could not remove the native-icon recovery marker.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Could not remove the native-icon recovery marker.", exception);
        }
    }

    private static IntPtr FindShellView()
    {
        var programManager = FindWindow("Progman", null);
        var shellView = FindWindowEx(programManager, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (shellView != IntPtr.Zero)
        {
            return shellView;
        }

        EnumWindows((window, _) =>
        {
            shellView = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            return shellView == IntPtr.Zero;
        }, IntPtr.Zero);
        return shellView;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);
}

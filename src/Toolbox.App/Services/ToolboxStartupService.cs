using Microsoft.Win32;

namespace PersonalToolbox.Services;

public sealed class ToolboxStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PersonalToolbox";
    private static readonly string[] LegacyValueNames =
    [
        "MouseRing",
        "DesktopOrganizer",
        "Cursor Gallery",
        "CursorSkinManager",
        "com.cursor-skin-manager.desktop",
    ];

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            key.SetValue(ValueName, $"\"{executablePath}\" --minimized");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    public bool RemoveLegacyEntries()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            foreach (var valueName in LegacyValueNames)
            {
                key?.DeleteValue(valueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}

using Microsoft.Win32;

namespace DesktopOrganizer.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopOrganizer";

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    return false;
                }

                key.SetValue(ValueName, $"\"{executablePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Could not update Windows startup registration.", exception);
            return false;
        }
        catch (System.Security.SecurityException exception)
        {
            AppLog.Error("Windows startup registration is not permitted.", exception);
            return false;
        }
    }
}

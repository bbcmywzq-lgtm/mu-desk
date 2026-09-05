using System.Diagnostics;
using System.IO;

namespace LightPet.App.Services;

internal enum PersonalToolsPage
{
    Reminder,
    Note,
}

/// <summary>
/// Process boundary between the pet and the standalone reminder/note tool.
/// LightPet does not reference the tool's UI, storage, scheduler or core assembly.
/// </summary>
internal sealed class PersonalToolsLauncher
{
    private const string ExecutableName = "ReminderNotes.exe";

    public void EnsureBackgroundRunning() => TryLaunch("--background");

    public bool TryOpen(PersonalToolsPage page) =>
        TryLaunch(page == PersonalToolsPage.Note ? "--note" : "--reminder");

    private static bool TryLaunch(string argument)
    {
        var executable = FindExecutable();
        if (executable is null)
        {
            return false;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo(executable, argument)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable),
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string? FindExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("PERSONALTOOLS_EXE");
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, "tools", "ReminderNotes", ExecutableName),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "ReminderNotes", ExecutableName)),
            Path.Combine(AppContext.BaseDirectory, ExecutableName),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "PersonalTools.App", "bin", "Release", "net10.0-windows", ExecutableName)),
        };

        return candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }
}

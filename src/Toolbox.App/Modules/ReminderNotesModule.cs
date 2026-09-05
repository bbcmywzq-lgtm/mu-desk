using System.Diagnostics;
using System.IO;

namespace PersonalToolbox.Modules;

public enum ReminderNotesPage
{
    Reminder,
    Note,
}

/// <summary>
/// Thin process client for the reminder/note worker. ReminderNotes keeps its UI,
/// data and scheduler, while MU Desk owns the ordinary tray and process lifecycle.
/// </summary>
public sealed class ReminderNotesModule : IDisposable
{
    private const string ExecutableName = "ReminderNotes.exe";
    private bool _managed;

    public bool StartBackground()
    {
        _managed = TryLaunch("--hosted");
        return _managed;
    }

    public bool Open(ReminderNotesPage page) =>
        TryLaunch(page == ReminderNotesPage.Note ? "--note" : "--reminder");

    public void Dispose()
    {
        if (_managed)
        {
            TryLaunch("--exit");
            _managed = false;
        }
    }

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

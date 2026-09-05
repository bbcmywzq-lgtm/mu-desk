using System.IO;
using System.Text;

namespace DesktopOrganizer.Services;

public static class AppLog
{
    private const long MaximumLogLength = 1_048_576;
    private static readonly object SyncRoot = new();
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopOrganizer");
    private static readonly string LogPath = Path.Combine(DataDirectory, "app.log");
    private static readonly string PreviousLogPath = Path.Combine(DataDirectory, "app.previous.log");

    public static void Information(string message) => Write("INFO", message, null);

    public static void Warning(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(DataDirectory);
                RotateIfNeeded();

                var builder = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("O"))
                    .Append(" [")
                    .Append(level)
                    .Append("] ")
                    .AppendLine(message);
                if (exception is not null)
                {
                    builder.AppendLine(exception.ToString());
                }

                File.AppendAllText(LogPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Logging must never make the desktop unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging must never make the desktop unavailable.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaximumLogLength)
        {
            return;
        }

        File.Move(LogPath, PreviousLogPath, true);
    }
}

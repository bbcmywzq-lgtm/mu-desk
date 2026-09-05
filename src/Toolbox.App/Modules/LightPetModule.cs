using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace PersonalToolbox.Modules;

/// <summary>
/// Hosts LightPet as a lightweight companion worker while MU Desk owns the
/// ordinary tray, startup entry and module lifecycle.
/// </summary>
public sealed class LightPetModule : IDisposable
{
    private const string ExecutableName = "LightPet.exe";
    private const string CommandPipeName = "LightPet.CommandPipe";
    private bool _visible;
    private bool _managed;

    public event EventHandler? StateChanged;

    public bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName("LightPet");
            try
            {
                return processes.Any(process => !process.HasExited);
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
    }

    public bool IsVisible => IsRunning && _visible;

    public bool Start()
    {
        var success = IsRunning ? SendOrLaunch("hosted") : TryLaunch("--hosted");
        if (success)
        {
            _managed = true;
            _visible = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        return success;
    }

    public void Stop()
    {
        if (_managed && IsRunning)
        {
            SendOrLaunch("exit");
        }
        _managed = false;
        _visible = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool ToggleVisibility()
    {
        if (!IsRunning && !Start())
        {
            return false;
        }

        var command = _visible ? "hide" : "show";
        if (!SendOrLaunch(command))
        {
            return false;
        }
        _visible = !_visible;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Show()
    {
        if (!IsRunning && !Start())
        {
            return false;
        }
        var success = SendOrLaunch("show");
        if (success)
        {
            _visible = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        return success;
    }

    public bool OpenSettings()
    {
        if (!IsRunning && !Start())
        {
            return false;
        }
        var success = SendOrLaunch("settings");
        if (success)
        {
            _visible = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        return success;
    }

    public void Dispose() => Stop();

    private static bool SendOrLaunch(string command) =>
        TrySend(command) || TryLaunch($"--{command}");

    private static bool TrySend(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", CommandPipeName, PipeDirection.Out);
            pipe.Connect(450);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
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
        var configured = Environment.GetEnvironmentVariable("LIGHTPET_EXE");
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, "tools", "LightPet", ExecutableName),
            Path.Combine(AppContext.BaseDirectory, ExecutableName),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LightPet.App", "bin", "Release", "net10.0-windows", ExecutableName)),
        };
        return candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }
}

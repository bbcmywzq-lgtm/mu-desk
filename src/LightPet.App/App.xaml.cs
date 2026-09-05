using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;
using LightPet.App.Services;
using LightPet.App.Views;
using LightPet.Core.Packs;

namespace LightPet.App;

public partial class App : System.Windows.Application
{
    private const string CommandPipeName = "LightPet.CommandPipe";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private readonly CancellationTokenSource _exitCancellation = new();
    private MainWindow? _petWindow;
    private bool _hostedByToolbox;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var command = ParseCommand(e.Args);
        _hostedByToolbox = string.Equals(command, "hosted", StringComparison.Ordinal);
        var qaWindow = string.Equals(
            Environment.GetEnvironmentVariable("LIGHTPET_QA_WINDOW"),
            "1",
            StringComparison.Ordinal);
        var mutexName = qaWindow
            ? $@"Local\LightPet.QA.{Environment.ProcessId}"
            : @"Local\LightPet.SingleInstance";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            SendCommandToRunningInstance(command);
            Shutdown(0);
            return;
        }

        try
        {
            var qaDataRoot = Environment.GetEnvironmentVariable("LIGHTPET_QA_DATA_ROOT");
            var isolatedQa = string.Equals(
                Environment.GetEnvironmentVariable("LIGHTPET_QA_WINDOW"),
                "1",
                StringComparison.Ordinal);
            var userDataRoot = string.IsNullOrWhiteSpace(qaDataRoot)
                ? isolatedQa
                    ? Path.Combine(Path.GetTempPath(), "LightPet-QA", Environment.ProcessId.ToString())
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "LightPet")
                : Path.GetFullPath(qaDataRoot);
            if (string.IsNullOrWhiteSpace(qaDataRoot) && !isolatedQa)
            {
                MigratePortableDataIfNeeded(userDataRoot);
            }
            var settingsStore = new SettingsStore(userDataRoot);
            var settings = settingsStore.Load();
            var petsRoot = Path.Combine(AppContext.BaseDirectory, "pets");
            var packs = new List<LoadedPetPack>();
            foreach (var path in Directory.EnumerateDirectories(petsRoot)
                         .Where(path => File.Exists(Path.Combine(path, "pet.json"))))
            {
                try
                {
                    packs.Add(PetPackLoader.Load(path));
                }
                catch (PetPackException)
                {
                    // A broken optional pack must not prevent a known-good pack from starting.
                }
            }
            if (packs.Count == 0)
            {
                throw new InvalidOperationException("没有找到可用的角色包。");
            }

            var pack = packs.FirstOrDefault(item => string.Equals(
                    item.Manifest.Id,
                    settings.PackId,
                    StringComparison.OrdinalIgnoreCase))
                ?? packs.FirstOrDefault(item => string.Equals(
                    item.Manifest.Id,
                    "violet-alex-benchmark",
                    StringComparison.OrdinalIgnoreCase))
                ?? packs[0];
            var window = new MainWindow(
                pack,
                packs,
                settingsStore,
                hostedByToolbox: _hostedByToolbox);
            _petWindow = window;
            MainWindow = window;
            window.Show();
            _ = ListenForCommandsAsync(_exitCancellation.Token);
            HandleCommand(command);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Pal 启动失败。\n\n{exception.Message}",
                "Pal",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitCancellation.Cancel();
        _exitCancellation.Dispose();
        ReleaseSingleInstanceMutex();
        base.OnExit(e);
    }

    private async Task ListenForCommandsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    CommandPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var command = await reader.ReadLineAsync(cancellationToken);
                await Dispatcher.InvokeAsync(() => HandleCommand(command));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A short-lived controller disconnected; accept the next command.
            }
        }
    }

    private void HandleCommand(string? command)
    {
        switch (command?.ToLowerInvariant())
        {
            case "hosted":
                _hostedByToolbox = true;
                _petWindow?.SetHostedMode();
                break;
            case "show":
                _petWindow?.ShowPetFromHost();
                break;
            case "hide":
                _petWindow?.HidePetFromHost();
                break;
            case "settings":
                _petWindow?.OpenSettingsFromHost();
                break;
            case "exit":
                _petWindow?.ExitFromHost();
                break;
        }
    }

    private static string ParseCommand(IReadOnlyList<string> args)
    {
        if (args.Any(value => string.Equals(value, "--hosted", StringComparison.OrdinalIgnoreCase)))
        {
            return "hosted";
        }
        foreach (var command in new[] { "show", "hide", "settings", "exit" })
        {
            if (args.Any(value => string.Equals(value, $"--{command}", StringComparison.OrdinalIgnoreCase)))
            {
                return command;
            }
        }
        return "show";
    }

    private static void SendCommandToRunningInstance(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", CommandPipeName, PipeDirection.Out);
            pipe.Connect(900);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(command);
        }
        catch (IOException)
        {
            // The existing instance may still be starting; the controller exits safely.
        }
        catch (TimeoutException)
        {
            // Avoid blocking a second launch when the first process is shutting down.
        }
    }

    public void Restart()
    {
        // A named mutex remains discoverable until every handle is closed.
        // Releasing ownership alone is not enough here: a replacement process
        // can observe createdNew=false while this process is still shutting down
        // and incorrectly treat itself as a second instance. Dispose the handle
        // before starting the replacement so role/pack switches are reliable.
        ReleaseSingleInstanceMutex();

        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定 LightPet 可执行文件路径。");
        _ = Process.Start(new ProcessStartInfo(
            executable,
            _hostedByToolbox ? "--hosted" : string.Empty)
        {
            UseShellExecute = true,
        });
        Shutdown();
    }

    private void ReleaseSingleInstanceMutex()
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }

    private static void MigratePortableDataIfNeeded(string userDataRoot)
    {
        var oldDataRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));
        var newDataRoot = Path.GetFullPath(Path.Combine(userDataRoot, "data"));
        if (!Directory.Exists(oldDataRoot) ||
            string.Equals(oldDataRoot, newDataRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            // Copy instead of moving: the portable folder and LocalAppData are
            // commonly on different volumes. Existing destination files win so a
            // stale portable backup can never overwrite newer reminders or notes.
            foreach (var sourcePath in Directory.EnumerateFiles(
                         oldDataRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(oldDataRoot, sourcePath);
                var destinationPath = Path.Combine(newDataRoot, relativePath);
                if (File.Exists(destinationPath))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: false);
            }
        }
        catch (IOException)
        {
            // A read-only or in-use portable data folder must not block startup.
        }
        catch (UnauthorizedAccessException)
        {
            // A read-only or in-use portable data folder must not block startup.
        }
    }
}

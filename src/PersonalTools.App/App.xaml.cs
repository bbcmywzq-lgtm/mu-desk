using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using PersonalTools.App.Services;
using PersonalTools.App.Views;
using PersonalTools.Core;

namespace PersonalTools.App;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\ReminderNotes.SingleInstance";
    private const string CommandPipeName = "ReminderNotes.CommandPipe";
    private readonly CancellationTokenSource _exitCancellation = new();
    private Mutex? _instanceMutex;
    private QuickToolsWindow? _window;
    private ReminderSchedulerService? _scheduler;
    private PersonalToolsTrayIconService? _tray;
    private IEntryProvider? _entries;
    private DesktopCardManager? _desktopCards;
    private bool _hostedByToolbox;
    private readonly Queue<EntryItem> _alertQueue = new();
    private ReminderAlertWindow? _activeAlert;
    private string _commandPipeName = CommandPipeName;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var command = ParseCommand(e.Args);
        _hostedByToolbox = command.Kind == ToolCommandKind.HostedBackground;
        var instanceSuffix = GetInstanceSuffix();
        _commandPipeName = CommandPipeName + instanceSuffix;
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName + instanceSuffix, out var createdNew);
        if (!createdNew)
        {
            SendCommandToRunningInstance(command, _commandPipeName);
            Shutdown();
            return;
        }

        try
        {
            var dataRoot = GetDataRoot();
            MigrateLegacyData(dataRoot);
            var dataDirectory = Path.Combine(dataRoot, "data");
            Directory.CreateDirectory(dataDirectory);
            _entries = new LocalJsonEntryProvider(Path.Combine(dataDirectory, "entries.json"));
            await EntryMigrationService.MigrateAsync(dataDirectory, _entries);
            _desktopCards = new DesktopCardManager(_entries);

            _window = new QuickToolsWindow(_entries, _desktopCards);
            _window.Closing += (_, args) =>
            {
                if (!_exitCancellation.IsCancellationRequested)
                {
                    args.Cancel = true;
                    _window.Hide();
                }
            };
            _desktopCards.OpenRequested += async (_, id) => await _window.EditEntryAsync(id);
            if (!_hostedByToolbox)
            {
                _tray = new PersonalToolsTrayIconService(
                    () => ShowPage(ToolPage.Reminder),
                    ExitApplication);
            }
            _scheduler = new ReminderSchedulerService(_entries);
            _scheduler.ReminderTriggered += (_, args) => Dispatcher.Invoke(() =>
            {
                _tray?.ShowReminder(args.Reminder.Content);
                _alertQueue.Enqueue(args.Reminder);
                ShowNextAlert();
                _ = RefreshViewsAsync();
            });
            _scheduler.Start();
            await _desktopCards.SynchronizeAsync();
            _ = ListenForCommandsAsync(_exitCancellation.Token);

            await HandleCommandAsync(command);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Memo 启动失败。\n\n{exception.Message}",
                "Memo",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ExitApplication();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _scheduler?.Dispose();
        _desktopCards?.Dispose();
        _activeAlert?.Close();
        _tray?.Dispose();
        _exitCancellation.Cancel();
        _exitCancellation.Dispose();
        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The startup path did not own the mutex.
            }
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }

    private void ShowPage(ToolPage page)
    {
        if (_window is null)
        {
            return;
        }

        if (page == ToolPage.Note)
        {
            _window.ShowNotePage();
        }
        else
        {
            _window.ShowReminderPage();
        }
    }

    private void ShowNextAlert()
    {
        if (_activeAlert is not null || _alertQueue.Count == 0)
        {
            return;
        }

        _activeAlert = new ReminderAlertWindow(_alertQueue.Dequeue());
        _activeAlert.ActionSelected += OnAlertActionSelected;
        _activeAlert.Show();
    }

    private async void OnAlertActionSelected(ReminderAlertAction action)
    {
        if (_activeAlert is null || _entries is null)
        {
            return;
        }

        var entry = _activeAlert.Entry;
        try
        {
            switch (action)
            {
                case ReminderAlertAction.Complete:
                    await _entries.CompleteAsync(entry.Id);
                    break;
                case ReminderAlertAction.SnoozeTenMinutes:
                    await _entries.SnoozeAsync(entry.Id, DateTimeOffset.UtcNow.AddMinutes(10));
                    break;
                case ReminderAlertAction.SnoozeOneHour:
                    await _entries.SnoozeAsync(entry.Id, DateTimeOffset.UtcNow.AddHours(1));
                    break;
                case ReminderAlertAction.Open:
                    ShowPage(ToolPage.Reminder);
                    if (_window is not null)
                    {
                        await _window.EditEntryAsync(entry.Id);
                    }
                    break;
            }
        }
        finally
        {
            _activeAlert.ActionSelected -= OnAlertActionSelected;
            _activeAlert = null;
            await RefreshViewsAsync();
            ShowNextAlert();
        }
    }

    private async Task RefreshViewsAsync()
    {
        if (_desktopCards is not null)
        {
            await _desktopCards.SynchronizeAsync();
        }
        if (_window is not null)
        {
            await _window.RefreshAsync();
        }
    }

    private async Task ListenForCommandsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _commandPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var value = await reader.ReadLineAsync(cancellationToken);
                var command = DeserializeCommand(value);
                var handling = await Dispatcher.InvokeAsync(() => HandleCommandAsync(command));
                await handling;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A caller disconnected before completing a command; accept the next one.
            }
            catch (JsonException)
            {
                // Ignore malformed local commands and keep the command server alive.
            }
        }
    }

    private static void SendCommandToRunningInstance(ToolCommand command, string pipeName)
    {
        if (command.Kind == ToolCommandKind.Background)
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                pipe.Connect(150);
                using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine(JsonSerializer.Serialize(command));
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (TimeoutException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static ToolCommand ParseCommand(IReadOnlyList<string> args)
    {
        if (HasOption(args, "--hosted"))
        {
            return new ToolCommand(ToolCommandKind.HostedBackground);
        }
        if (HasOption(args, "--exit"))
        {
            return new ToolCommand(ToolCommandKind.Exit);
        }
        if (HasOption(args, "--background"))
        {
            return new ToolCommand(ToolCommandKind.Background);
        }

        var pin = HasOption(args, "--pin");
        var noteContent = OptionValue(args, "--create-note") ?? InlineValue(args, "--note");
        if (!string.IsNullOrWhiteSpace(noteContent))
        {
            return new ToolCommand(ToolCommandKind.Create, noteContent.Trim(), Pin: pin, ShowWindow: true);
        }

        var reminderContent = OptionValue(args, "--remind") ?? OptionValue(args, "--create-reminder");
        if (!string.IsNullOrWhiteSpace(reminderContent))
        {
            var atText = OptionValue(args, "--at");
            if (!DateTimeOffset.TryParse(atText, out var dueAt))
            {
                throw new ArgumentException("--remind 需要可解析的 --at 时间。", nameof(args));
            }
            return new ToolCommand(ToolCommandKind.Create, reminderContent.Trim(), dueAt, pin, ShowWindow: true);
        }

        return HasOption(args, "--note") || HasOption(args, "--capture")
            ? new ToolCommand(ToolCommandKind.ShowNote)
            : new ToolCommand(ToolCommandKind.ShowReminder);
    }

    private static ToolCommand DeserializeCommand(string? value)
    {
        if (string.Equals(value, "note", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolCommand(ToolCommandKind.ShowNote);
        }
        if (string.Equals(value, "reminder", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolCommand(ToolCommandKind.ShowReminder);
        }
        return JsonSerializer.Deserialize<ToolCommand>(value ?? string.Empty)
            ?? new ToolCommand(ToolCommandKind.ShowReminder);
    }

    private async Task HandleCommandAsync(ToolCommand command)
    {
        switch (command.Kind)
        {
            case ToolCommandKind.HostedBackground:
                _hostedByToolbox = true;
                _tray?.Dispose();
                _tray = null;
                return;
            case ToolCommandKind.Exit:
                ExitApplication();
                return;
            case ToolCommandKind.Background:
                return;
            case ToolCommandKind.ShowNote:
                ShowPage(ToolPage.Note);
                return;
            case ToolCommandKind.ShowReminder:
                ShowPage(ToolPage.Reminder);
                return;
            case ToolCommandKind.Create when _entries is not null:
                await _entries.CreateAsync(new CreateEntryCommand(
                    command.Content ?? throw new ArgumentException("随记内容不能为空。"),
                    DueAt: command.DueAt,
                    PinToDesktop: command.Pin,
                    Source: "external-command"));
                await RefreshViewsAsync();
                if (command.ShowWindow)
                {
                    ShowPage(command.DueAt is null ? ToolPage.Note : ToolPage.Reminder);
                }
                if (_scheduler is not null)
                {
                    await _scheduler.CheckNowAsync();
                }
                return;
        }
    }

    private static bool HasOption(IReadOnlyList<string> args, string option) =>
        args.Any(value => string.Equals(value, option, StringComparison.OrdinalIgnoreCase));

    private static string? OptionValue(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase) &&
                !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }
        return null;
    }

    private static string? InlineValue(IReadOnlyList<string> args, string option)
    {
        var value = OptionValue(args, option);
        return string.Equals(value, "--at", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static string GetDataRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("PERSONALTOOLS_DATA_ROOT");
        return string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReminderNotes")
            : Path.GetFullPath(overrideRoot);
    }

    private static string GetInstanceSuffix()
    {
        var value = Environment.GetEnvironmentVariable("PERSONALTOOLS_INSTANCE_SUFFIX");
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var safe = new string(value.Where(char.IsLetterOrDigit).Take(32).ToArray());
        return safe.Length == 0 ? string.Empty : $".{safe}";
    }

    private static void MigrateLegacyData(string dataRoot)
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LightPet",
            "data");
        var destination = Path.Combine(dataRoot, "data");
        if (!Directory.Exists(legacy))
        {
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var fileName in new[] { "reminders.json", "notes.json" })
        {
            var sourcePath = Path.Combine(legacy, fileName);
            var destinationPath = Path.Combine(destination, fileName);
            if (File.Exists(sourcePath) && !File.Exists(destinationPath))
            {
                File.Copy(sourcePath, destinationPath, overwrite: false);
            }
        }
    }

    private void ExitApplication()
    {
        if (_exitCancellation.IsCancellationRequested)
        {
            return;
        }

        _exitCancellation.Cancel();
        _window?.Close();
        Shutdown();
    }

    private sealed record ToolCommand(
        ToolCommandKind Kind,
        string? Content = null,
        DateTimeOffset? DueAt = null,
        bool Pin = false,
        bool ShowWindow = false);

    private enum ToolCommandKind { Background, HostedBackground, ShowReminder, ShowNote, Create, Exit }
    private enum ToolPage { Reminder, Note }
}

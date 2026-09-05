using System.IO;
using System.Windows;
using PersonalToolbox.Services;
using PersonalToolbox.Views;
using Toolbox.Core;

namespace PersonalToolbox.Modules;

public sealed class EffectCaptureModule : IDisposable
{
    private readonly EffectCaptureSettings _settings;
    private readonly Action _saveSettings;
    private readonly CapturePackageService _packageService = new();
    private readonly Func<EffectCapturePackageResult, CancellationToken, Task> _send;
    private readonly string _defaultOutputRoot;
    private bool _busy;

    public EffectCaptureModule(
        EffectCaptureSettings settings,
        Action saveSettings,
        Func<EffectCapturePackageResult, CancellationToken, Task>? send = null,
        string? defaultOutputRoot = null)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _send = send ?? ((_, _) => Task.FromException(new InvalidOperationException("Codex 入口尚未初始化。")));
        _defaultOutputRoot = string.IsNullOrWhiteSpace(defaultOutputRoot)
            ? GetDefaultOutputRoot()
            : Path.GetFullPath(defaultOutputRoot);
        CapturePackageService.CleanupStalePartialDirectories(_defaultOutputRoot, DateTimeOffset.Now.AddDays(-1));
    }

    public bool IsBusy => _busy;

    public event EventHandler? StateChanged;

    public event EventHandler? SettingsChanged;

    public event EventHandler<ModuleErrorEventArgs>? Error;

    public async void StartCapture()
    {
        if (_busy)
        {
            return;
        }

        var selectionWindow = new EffectRegionSelectionWindow(_settings.DefaultDurationSeconds);
        if (selectionWindow.ShowDialog() != true || selectionWindow.Result is null)
        {
            return;
        }

        var selection = selectionWindow.Result;
        _settings.DefaultDurationSeconds = selection.DurationSeconds;
        _saveSettings();
        _busy = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        EffectCaptureHudWindow? hud = null;
        EffectCaptureBorderWindow? border = null;
        EffectCaptureWindow? progressWindow = null;
        using var cancelSource = new CancellationTokenSource();
        using var stopSource = new CancellationTokenSource();
        try
        {
            hud = new EffectCaptureHudWindow(selection.Region);
            border = new EffectCaptureBorderWindow(selection.Region);
            hud.CancelRequested += (_, _) => cancelSource.Cancel();
            hud.StopRequested += (_, _) => stopSource.Cancel();
            border.Show();
            hud.Show();
            if (_settings.CountdownEnabled)
            {
                for (var count = 3; count >= 1; count--)
                {
                    hud.SetText($"{count} 秒后录制");
                    await Task.Delay(700, cancelSource.Token);
                }
            }

            hud.SetText($"录制中 · {selection.DurationSeconds} 秒");
            hud.SetRecording(true);
            progressWindow = new EffectCaptureWindow();
            progressWindow.CaptureAgainRequested += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(StartCapture);
            var progress = new Progress<(string Stage, double Progress)>(value =>
            {
                if (value.Stage != "正在录制" && !progressWindow.IsVisible)
                {
                    hud?.Close();
                    hud = null;
                    border?.Close();
                    border = null;
                    progressWindow.Show();
                }

                progressWindow.SetProgress(value.Stage, value.Progress);
            });
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                progressWindow.CancellationToken,
                cancelSource.Token);
            var result = await _packageService.CreateAsync(
                selection.Region,
                TimeSpan.FromSeconds(selection.DurationSeconds),
                OutputRoot,
                progress,
                linkedCancellation.Token,
                stopSource.Token);
            hud?.Close();
            hud = null;
            border?.Close();
            border = null;
            if (!progressWindow.IsVisible)
            {
                progressWindow.Show();
            }
            progressWindow.ShowResult(result, _send);
            progressWindow.Activate();
        }
        catch (OperationCanceledException)
        {
            progressWindow?.Close();
        }
        catch (Exception exception)
        {
            progressWindow ??= new EffectCaptureWindow();
            if (!progressWindow.IsVisible)
            {
                progressWindow.Show();
            }

            progressWindow.ShowFailure(exception.Message);
            Error?.Invoke(this, new ModuleErrorEventArgs(exception.Message));
        }
        finally
        {
            hud?.Close();
            border?.Close();
            _busy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool OpenSettings(Window? owner = null)
    {
        var window = new EffectCaptureSettingsWindow(_settings, _defaultOutputRoot) { Owner = owner };
        if (window.ShowDialog() != true)
        {
            return false;
        }

        _saveSettings();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
    }

    private string OutputRoot => string.IsNullOrWhiteSpace(_settings.OutputRoot) ? _defaultOutputRoot : Path.GetFullPath(_settings.OutputRoot);

    private static string GetDefaultOutputRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MU Desk",
        "effect-capture",
        "captures");
}

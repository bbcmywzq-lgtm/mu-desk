using System.IO;
using PersonalToolbox.Native;
using PersonalToolbox.Views;
using Toolbox.Core;

namespace PersonalToolbox.Modules;

public sealed class CispQuestionBankModule : IToolModule
{
    private readonly string _dataRoot;
    private CispQuestionBankWindow? _window;

    public CispQuestionBankModule()
    {
        _dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MU Desk",
            "cisp-question-bank");
    }

    public string Id => "cisp-question-bank";

    public string DisplayName => "CISP 题库";

    public string Description => "普通 CISP（CISE/CISO）公开题库练习。";

    public bool IsRunning { get; private set; }

    public bool IsPaused => false;

    public event EventHandler? StateChanged;

    public event EventHandler<ModuleErrorEventArgs>? Error;

    public bool Start()
    {
        IsRunning = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Stop()
    {
        _window?.Close();
        _window = null;
        if (IsRunning)
        {
            IsRunning = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetPaused(bool paused)
    {
    }

    public bool OpenSettings()
        => OpenWindow(startInSetMode: false);

    public bool OpenLatestSets()
        => OpenWindow(startInSetMode: true);

    private bool OpenWindow(bool startInSetMode)
    {
        try
        {
            if (_window is { IsVisible: true })
            {
                _window.SelectPracticeMode(startInSetMode);
                if (_window.WindowState == System.Windows.WindowState.Minimized)
                {
                    _window.WindowState = System.Windows.WindowState.Normal;
                }

                _window.Activate();
                _window.Topmost = true;
                _window.Topmost = false;
                _window.Focus();
                return true;
            }

            Directory.CreateDirectory(_dataRoot);
            var progressStore = new CispProgressStore(Path.Combine(_dataRoot, "progress.json"));
            var assetRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "Cisp");
            _window = new CispQuestionBankWindow(
                Path.Combine(_dataRoot, "public-bank.md"),
                progressStore,
                Path.Combine(assetRoot, "public-bank.md"),
                Path.Combine(assetRoot, "pic"),
                Path.Combine(assetRoot, "PdfSets"),
                Path.Combine(assetRoot, "2026-sets.json"),
                startInSetMode);
            TaskbarWindowIdentity.Apply(_window, TaskbarWindowIdentity.CispQuestionBank);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            _window.Activate();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Error?.Invoke(this, new ModuleErrorEventArgs(exception.Message));
            return false;
        }
    }

    public void Dispose() => Stop();
}

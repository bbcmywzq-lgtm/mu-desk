using System.IO;
using System.Windows;
using PersonalToolbox.Views;
using Toolbox.Core;

namespace PersonalToolbox.Modules;

public sealed class FileShelfModule : IToolModule
{
    private readonly FileShelfSettings _settings;
    private readonly Action _saveSettings;
    private readonly FileShelfStore _store;
    private FileShelfDocument _document = new();
    private FileShelfDocument? _undoDocument;
    private FileShelfWindow? _window;

    public FileShelfModule(FileShelfSettings settings, Action saveSettings, FileShelfStore? store = null)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _store = store ?? new FileShelfStore();
    }

    public string Id => "file-shelf";

    public string DisplayName => "Mujun Drop";

    public string Description => "文件先搁一下，换个窗口再拖走";

    public bool IsRunning { get; private set; }

    public bool IsPaused { get; private set; }

    public bool IsExpanded => _window?.IsExpanded == true;

    public int ItemCount => _document.ItemCount;

    public int PinnedCount => _document.PinnedCount;

    public bool CanUndo => _undoDocument is not null;

    public string DataFolder => _store.DataFolder;

    public event EventHandler? StateChanged;

    public event EventHandler? DocumentChanged;

    public event EventHandler<ModuleErrorEventArgs>? Error;

    public bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        _settings.Normalize();
        var load = _store.Load();
        _document = load.Document;
        IsRunning = true;
        IsPaused = false;
        EnsureWindow();
        _window!.ShowCollapsed();
        if (!string.IsNullOrWhiteSpace(load.ErrorMessage))
        {
            _window.ShowStatus(load.ErrorMessage, isError: !load.RecoveredFromBackup);
            if (!load.RecoveredFromBackup)
            {
                Error?.Invoke(this, new ModuleErrorEventArgs("Drop 数据无法读取；原文件已保留。"));
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _window?.CloseForModuleStop();
        _window = null;
        IsRunning = false;
        IsPaused = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPaused(bool paused)
    {
        if (!IsRunning || IsPaused == paused)
        {
            return;
        }

        IsPaused = paused;
        if (paused)
        {
            _window?.Hide();
        }
        else
        {
            EnsureWindow();
            _window!.ShowCollapsed();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool OpenSettings()
    {
        ShowShelf();
        return true;
    }

    public void ShowShelf()
    {
        if (!IsRunning || IsPaused)
        {
            return;
        }

        EnsureWindow();
        _window!.ShowExpanded(activate: true);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleShelf()
    {
        if (!IsRunning || IsPaused)
        {
            return;
        }

        EnsureWindow();
        if (_window!.IsExpanded)
        {
            _window.ShowCollapsed();
        }
        else
        {
            _window.ShowExpanded(activate: true);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public FileShelfDocument Snapshot() => _document.Clone();

    public bool TryAddPaths(IEnumerable<string> paths, out string message)
    {
        var candidate = _document.Clone();
        var requested = paths.ToArray();
        var availableSlots = FileShelfDocument.MaximumItemCount - candidate.ItemCount;
        if (availableSlots <= 0)
        {
            message = $"货架已达到 {FileShelfDocument.MaximumItemCount} 项上限。";
            return false;
        }

        try
        {
            var batch = FileShelfOperations.AddBatch(candidate, requested);
            if (!Commit(candidate, out message))
            {
                return false;
            }

            message = batch.Items.Count < requested.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                ? $"已搁下 {batch.Items.Count} 项；其余项目无效、重复或超过上限。"
                : $"已搁下 {batch.Items.Count} 项。";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            message = exception.Message;
            return false;
        }
    }

    public bool TrySetPinned(string itemId, bool pinned, out string message)
    {
        var candidate = _document.Clone();
        var item = candidate.Batches.SelectMany(batch => batch.Items)
            .FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            message = "这个项目已经不在货架上。";
            return false;
        }

        item.IsPinned = pinned;
        return Commit(candidate, out message);
    }

    public bool TryRemove(IEnumerable<string> itemIds, bool includePinned, out string message)
    {
        var candidate = _document.Clone();
        var before = _document.Clone();
        var removed = FileShelfOperations.RemoveItems(candidate, itemIds, includePinned);
        if (removed.Count == 0)
        {
            message = "没有可移除的项目。";
            return false;
        }

        if (!Commit(candidate, out message))
        {
            return false;
        }

        _undoDocument = before;
        message = $"已从货架移除 {removed.Count} 项；原文件没有变化。";
        NotifyDocumentChanged();
        return true;
    }

    public bool TryClearUnpinned(out string message)
    {
        var candidate = _document.Clone();
        var before = _document.Clone();
        var removed = FileShelfOperations.ClearUnpinned(candidate);
        if (removed.Count == 0)
        {
            message = "没有未固定项目可清空。";
            return false;
        }

        if (!Commit(candidate, out message))
        {
            return false;
        }

        _undoDocument = before;
        message = $"已清空 {removed.Count} 个未固定项目；原文件没有变化。";
        NotifyDocumentChanged();
        return true;
    }

    public bool TryUndo(out string message)
    {
        if (_undoDocument is null)
        {
            message = "当前没有可撤销的移除。";
            return false;
        }

        var candidate = _undoDocument.Clone();
        if (!Commit(candidate, out message))
        {
            return false;
        }

        _undoDocument = null;
        message = "已恢复最近一次移除。";
        NotifyDocumentChanged();
        return true;
    }

    public void UpdateDockPlacement(string dockSide, string? deviceName, double verticalPosition)
    {
        _settings.DockSide = dockSide;
        _settings.DisplayDeviceName = deviceName;
        _settings.VerticalPosition = verticalPosition;
        _settings.Normalize();
        _saveSettings();
    }

    public void Dispose() => Stop();

    private bool Commit(FileShelfDocument candidate, out string message)
    {
        if (!_store.TrySave(candidate, out var error))
        {
            message = error ?? "Drop 无法保存。";
            Error?.Invoke(this, new ModuleErrorEventArgs("Drop 无法保存；这次操作已回滚。"));
            return false;
        }

        _document = candidate;
        message = string.Empty;
        NotifyDocumentChanged();
        return true;
    }

    private void NotifyDocumentChanged()
    {
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new FileShelfWindow(this, _settings);
        _window.ExpansionChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _window.Closed += (_, _) => _window = null;
    }
}

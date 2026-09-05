using System.Drawing;
using System.Windows.Forms;

namespace MouseRing.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly Icon _icon;
    private bool _isPaused;

    public TrayIconService(Action openSettings, Action exit)
    {
        var menu = new ContextMenuStrip();
        _pauseItem = new ToolStripMenuItem("暂停轮盘");
        _pauseItem.Click += (_, _) => TogglePaused();
        menu.Items.Add(_pauseItem);
        menu.Items.Add("设置", null, (_, _) => openSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());

        var processPath = Environment.ProcessPath;
        _icon = (!string.IsNullOrWhiteSpace(processPath) ? Icon.ExtractAssociatedIcon(processPath) : null) ??
            (Icon)SystemIcons.Application.Clone();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Orbit",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => openSettings();
    }

    public bool IsPaused => _isPaused;

    public event Action<bool>? PauseChanged;

    public void ShowStartedNotice()
    {
        _notifyIcon.BalloonTipTitle = "Orbit 已启动";
        _notifyIcon.BalloonTipText = "按住右键并点一下左键，稍候移动鼠标选择。";
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void ShowError(string message)
    {
        _notifyIcon.BalloonTipTitle = "Orbit";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void TogglePaused()
    {
        _isPaused = !_isPaused;
        _pauseItem.Text = _isPaused ? "恢复轮盘" : "暂停轮盘";
        _notifyIcon.Text = _isPaused ? "Orbit（已暂停）" : "Orbit";
        PauseChanged?.Invoke(_isPaused);
    }
}

using System.Drawing;
using System.Windows.Forms;

namespace PersonalToolbox.Services;

public sealed class ToolboxTrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _mouseRingItem;
    private readonly ToolStripMenuItem _cueItem;
    private readonly ToolStripMenuItem _desktopOrganizerItem;
    private readonly ToolStripMenuItem _fileShelfItem;
    private readonly ToolStripMenuItem _lightPetItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly Icon _icon;

    public ToolboxTrayIcon(
        Action openToolbox,
        Action<bool> setMouseRingEnabled,
        Action openMouseRingSettings,
        Action<bool> setCueEnabled,
        Action openCueToolbar,
        Action openCueSettings,
        Action resetCue,
        Action startEffectCapture,
        Action openEffectCaptureSettings,
        Action openCispQuestionBank,
        Action openCursorGallery,
        Action openReminder,
        Action openNote,
        Action toggleFileShelf,
        Action<bool> setLightPetEnabled,
        Action toggleLightPetVisibility,
        Action openLightPetSettings,
        Action<bool> setDesktopOrganizerEnabled,
        Action showDesktopOrganizer,
        Action openDesktopOrganizerSettings,
        Action<bool> setPaused,
        Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开工具箱", null, (_, _) => openToolbox());
        menu.Items.Add(new ToolStripSeparator());
        _mouseRingItem = new ToolStripMenuItem("Orbit · 快捷轮盘") { CheckOnClick = true };
        _mouseRingItem.Click += (_, _) => setMouseRingEnabled(_mouseRingItem.Checked);
        menu.Items.Add(_mouseRingItem);
        menu.Items.Add("Orbit 设置…", null, (_, _) => openMouseRingSettings());
        menu.Items.Add(new ToolStripSeparator());
        _cueItem = new ToolStripMenuItem("Cue · 屏幕讲解") { CheckOnClick = true };
        _cueItem.Click += (_, _) => setCueEnabled(_cueItem.Checked);
        menu.Items.Add(_cueItem);
        menu.Items.Add("打开 Cue 工具栏", null, (_, _) => openCueToolbar());
        menu.Items.Add("Cue 设置…", null, (_, _) => openCueSettings());
        menu.Items.Add("复原 Cue 屏幕效果", null, (_, _) => resetCue());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Clip · 动态拾取…", null, (_, _) => startEffectCapture());
        menu.Items.Add("Clip 设置…", null, (_, _) => openEffectCaptureSettings());
        menu.Items.Add("CISP 题库与错题本…", null, (_, _) => openCispQuestionBank());
        menu.Items.Add("Tip · 光标装扮…", null, (_, _) => openCursorGallery());
        menu.Items.Add("Memo · 新建提醒…", null, (_, _) => openReminder());
        menu.Items.Add("Memo · 新建随记…", null, (_, _) => openNote());
        _fileShelfItem = new ToolStripMenuItem("Drop · 文件暂存…");
        _fileShelfItem.Click += (_, _) => toggleFileShelf();
        menu.Items.Add(_fileShelfItem);
        _lightPetItem = new ToolStripMenuItem("Pal · 桌面伙伴") { CheckOnClick = true };
        _lightPetItem.Click += (_, _) => setLightPetEnabled(_lightPetItem.Checked);
        menu.Items.Add(_lightPetItem);
        menu.Items.Add("显示 / 隐藏桌宠", null, (_, _) => toggleLightPetVisibility());
        menu.Items.Add("Pal 设置…", null, (_, _) => openLightPetSettings());
        _desktopOrganizerItem = new ToolStripMenuItem("Grid · 桌面整理") { CheckOnClick = true };
        _desktopOrganizerItem.Click += (_, _) => setDesktopOrganizerEnabled(_desktopOrganizerItem.Checked);
        menu.Items.Add(_desktopOrganizerItem);
        menu.Items.Add("显示 Grid", null, (_, _) => showDesktopOrganizer());
        menu.Items.Add("Grid 设置…", null, (_, _) => openDesktopOrganizerSettings());
        _pauseItem = new ToolStripMenuItem("暂停所有工具");
        _pauseItem.Click += (_, _) => setPaused(!_pauseItem.Checked);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出工具箱", null, (_, _) => exit());

        var processPath = Environment.ProcessPath;
        _icon = (!string.IsNullOrWhiteSpace(processPath) ? Icon.ExtractAssociatedIcon(processPath) : null) ??
            (Icon)SystemIcons.Application.Clone();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "MU Desk",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => openToolbox();
    }

    public void UpdateState(
        bool mouseRingEnabled,
        bool cueEnabled,
        bool desktopOrganizerEnabled,
        bool fileShelfEnabled,
        bool lightPetEnabled,
        bool paused)
    {
        _mouseRingItem.Checked = mouseRingEnabled;
        _cueItem.Checked = cueEnabled;
        _desktopOrganizerItem.Checked = desktopOrganizerEnabled;
        _fileShelfItem.Checked = fileShelfEnabled;
        _lightPetItem.Checked = lightPetEnabled;
        _pauseItem.Checked = paused;
        _pauseItem.Text = paused ? "恢复所有工具" : "暂停所有工具";
        _notifyIcon.Text = paused ? "MU Desk（已暂停）" : "MU Desk";
    }

    public void ShowStartedNotice()
    {
        _notifyIcon.BalloonTipTitle = "MU Desk 已加载";
        _notifyIcon.BalloonTipText = "九项本机工具已由一个入口统一加载。";
        _notifyIcon.ShowBalloonTip(3500);
    }

    public void ShowError(string message)
    {
        _notifyIcon.BalloonTipTitle = "MU Desk";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3500);
    }

    public void ShowInfo(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(4500);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}

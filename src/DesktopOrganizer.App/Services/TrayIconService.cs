using System.Drawing;
using System.Windows.Forms;

namespace DesktopOrganizer.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;

    public TrayIconService(MainWindow window)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("搜索所有文件", null, (_, _) => window.Dispatcher.Invoke(window.FocusSearch));
        menu.Items.Add("立即自动整理", null, (_, _) => window.Dispatcher.Invoke(window.ApplyRulesNow));
        menu.Items.Add("撤销上一次整理", null, (_, _) => window.Dispatcher.Invoke(window.UndoLastAction));
        menu.Items.Add("新建分区", null, (_, _) => window.Dispatcher.Invoke(window.CreateZone));
        menu.Items.Add("设置", null, (_, _) => window.Dispatcher.Invoke(window.OpenSettings));
        menu.Items.Add("导出备份", null, (_, _) => window.Dispatcher.Invoke(window.ExportData));
        menu.Items.Add("导入备份", null, (_, _) => window.Dispatcher.Invoke(window.ImportData));
        menu.Items.Add("重置布局", null, (_, _) => window.Dispatcher.Invoke(window.ResetPrototypeLayout));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => window.Dispatcher.Invoke(() =>
            (System.Windows.Application.Current as App)?.RequestShutdown()));

        var processPath = Environment.ProcessPath;
        _icon = (!string.IsNullOrWhiteSpace(processPath) ? Icon.ExtractAssociatedIcon(processPath) : null) ??
            (Icon)SystemIcons.Application.Clone();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Grid · 桌面各得其所",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => window.Dispatcher.Invoke(window.ShowAndActivate);
    }

    public void ShowStartedNotice()
    {
        _notifyIcon.BalloonTipTitle = "Grid 已启动";
        _notifyIcon.BalloonTipText = "应用正在桌面和通知区域运行。最小化其他窗口即可查看桌面分区。";
        _notifyIcon.ShowBalloonTip(3500);
    }

    public void ShowRunningNotice()
    {
        _notifyIcon.BalloonTipTitle = "Grid 已在运行";
        _notifyIcon.BalloonTipText = "已唤回现有实例；应用不会重复启动。";
        _notifyIcon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}

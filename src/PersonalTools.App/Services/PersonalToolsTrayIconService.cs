using System.Drawing;
using System.Windows.Forms;

namespace PersonalTools.App.Services;

internal sealed class PersonalToolsTrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _applicationIcon;
    private readonly Action _open;

    public PersonalToolsTrayIconService(Action open, Action exit)
    {
        _open = open;
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Memo", null, (_, _) => open());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        _applicationIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
            ?? (Icon)SystemIcons.Application.Clone();
        _icon = new NotifyIcon
        {
            Text = "Memo",
            Icon = _applicationIcon,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => open();
        _icon.BalloonTipClicked += (_, _) => open();
    }

    public void ShowReminder(string content)
    {
        _icon.ShowBalloonTip(10_000, "提醒", content, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _applicationIcon.Dispose();
    }
}

using System.Drawing;
using System.Windows.Forms;

namespace PicLoom.Host.Ui;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Action open;
    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenuStrip menu;
    private bool trayDisposed;

    public TrayApplicationContext(Action open)
    {
        this.open = open;

        var openItem = new ToolStripMenuItem("打开 PicLoom / Open");
        openItem.Click += (_, _) => this.open();

        var exitItem = new ToolStripMenuItem("退出 / Exit");
        exitItem.Click += (_, _) => ExitThread();

        menu = new ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(exitItem);

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PicLoom",
            ContextMenuStrip = menu,
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => this.open();
    }

    public void HideTray()
    {
        if (trayDisposed)
        {
            return;
        }

        trayDisposed = true;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }

    protected override void ExitThreadCore()
    {
        HideTray();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            HideTray();
            menu.Dispose();
        }

        base.Dispose(disposing);
    }
}

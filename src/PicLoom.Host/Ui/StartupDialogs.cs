using System.Windows.Forms;

namespace PicLoom.Host.Ui;

internal static class StartupDialogs
{
    private const string Caption = "PicLoom";

    public static void ShowStartupFailure() => Show(
        "PicLoom 启动失败。请检查端口是否被占用或数据目录是否可访问，然后重试。\n\n" +
        "PicLoom could not start. Check that the port is free and the data directory is accessible, then try again.");

    public static void ShowAlreadyRunning() => Show(
        "PicLoom 已在运行，但无法打开主界面。请关闭现有 PicLoom 托盘图标后重试。\n\n" +
        "PicLoom is already running, but its window could not be opened. Close the existing PicLoom tray icon and try again.");

    public static void ShowOpenFailure() => Show(
        "无法打开默认浏览器。请稍后从托盘菜单重试。\n\n" +
        "Could not open the default browser. Please retry from the tray menu.");

    public static void ShowFatalFailure() => Show(
        "PicLoom 遇到错误，即将退出。请重试。\n\n" +
        "PicLoom encountered an error and will exit. Please try again.");

    private static void Show(string message) =>
        MessageBox.Show(message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
}

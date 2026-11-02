namespace FullWidget.Shell;

sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon icon;

    public TrayIcon(App app)
    {
        icon = new NotifyIcon { Icon = AppIcon.Create(SystemInformation.SmallIconSize.Width), Text = Strings.AppName, Visible = true };
        // Left click opens the control center, right click the menu.
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) app.OpenControlCenter();
            else if (e.Button == MouseButtons.Right) app.ShowMenu(() => Menus.ForTray(app), Cursor.Position, fromTray: true);
        };
    }

    public void Balloon(string text) => icon.ShowBalloonTip(4000, Strings.AppName, text, ToolTipIcon.Info);

    /// A Windows notification on behalf of a widget (Windows shows it as a toast from FullWidget).
    public void Notify(string title, string text) => icon.ShowBalloonTip(6000, title, text.Length > 0 ? text : " ", ToolTipIcon.None);

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}

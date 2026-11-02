using System.Text.Json;
using FullWidget.Core;
using FullWidget.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static FullWidget.Native.Win32;

namespace FullWidget.Windows;

/// The drawn popup menu (ui/menu) used for the tray and for widget right-clicks.
/// One window is created at startup and reused, so a menu opens without delay.
///
/// Opening is a short handshake: the host sends the entries, the page lays them out and
/// reports its size, then the host places the window next to the cursor and shows it.
/// It closes on Esc, on a click elsewhere, or when another window becomes active.
sealed class MenuPopup : Form
{
    const string Host = "menu.fullwidget.local";

    readonly App app;
    readonly WebView2 web;
    readonly System.Windows.Forms.Timer outside = new() { Interval = 50 };
    readonly Dictionary<string, PopItem> byId = new();
    Func<List<PopItem>>? build;
    (Func<List<PopItem>> build, Point at, bool fromTray)? pending;
    Point anchor;
    bool ready, upward, alignRight;
    int dpi = 96;

    public MenuPopup(App app)
    {
        this.app = app;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Bounds = new Rectangle(-32000, -32000, 300, 200);
        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
        Controls.Add(web);
        outside.Tick += (_, _) => CloseIfClickedOutside();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Per-pixel transparency, as for widgets: rounded card and soft shadow over anything.
        var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(Handle, ref m);
        int corner = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
        uint border = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, 4);
    }

    /// Creates the web view while the window is still hidden.
    public async Task InitAsync()
    {
        _ = Handle;
        _ = web.Handle;
        await web.EnsureCoreWebView2Async(app.Env);
        var core = web.CoreWebView2;
        bool dev = app.Config.Data.DeveloperMode;
        core.Settings.AreDevToolsEnabled = dev;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        core.SetVirtualHostNameToFolderMapping(Host, Paths.MenuDir, CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnMessage;
        core.NewWindowRequested += (_, a) => a.Handled = true;
        core.NavigationStarting += (_, a) =>
        {
            if (!a.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) a.Cancel = true;
        };
        core.Navigate($"https://{Host}/index.html");
    }

    /// Opens a menu at a screen point. `build` is called again after every toggle, so the
    /// entries always show the current state.
    public void Open(Func<List<PopItem>> build, Point at, bool fromTray)
    {
        if (!ready) { pending = (build, at, fromTray); return; }
        if (Visible) HideMenu();
        this.build = build;
        anchor = at;
        dpi = DpiAt(at.X, at.Y);
        var wa = Screen.FromPoint(at).WorkingArea;
        upward = at.Y > wa.Top + wa.Height / 2;
        alignRight = fromTray || at.X > wa.Left + wa.Width * 3 / 4;
        Send("open", new { items = Index(build()), upward, alignRight });
    }

    List<PopItem> Index(List<PopItem> items)
    {
        byId.Clear();
        int n = 0;
        void Walk(List<PopItem> list)
        {
            foreach (var it in list)
            {
                it.Id = $"m{n++}";
                byId[it.Id] = it;
                if (it.Items != null) Walk(it.Items);
            }
        }
        Walk(items);
        return items;
    }

    void Place(double cssWidth, double cssHeight, double cssMargin)
    {
        double s = dpi / 96.0;
        int w = (int)Math.Ceiling(cssWidth * s), h = (int)Math.Ceiling(cssHeight * s), m = (int)Math.Round(cssMargin * s);
        var wa = Screen.FromPoint(anchor).WorkingArea;
        int x = alignRight ? anchor.X - w + m : anchor.X - m;
        int y = upward ? anchor.Y - h + m : anchor.Y - m;
        // keep the card itself on screen (the shadow margin may hang off the edge)
        x = Math.Clamp(x, wa.Left - m, Math.Max(wa.Left - m, wa.Right - w + m));
        y = Math.Clamp(y, wa.Top - m, Math.Max(wa.Top - m, wa.Bottom - h + m));
        Bounds = new Rectangle(x, y, w, h);
    }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try { msg = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson); }
        catch { return; }
        if (!msg.TryGetProperty("m", out var mEl)) return;
        var p = msg.TryGetProperty("p", out var pEl) ? pEl : default;

        switch (mEl.GetString())
        {
            case "ready":
                ready = true;
                if (pending is { } q) { pending = null; Open(q.build, q.at, q.fromTray); }
                break;

            case "size":
                Place(p.GetProperty("w").GetDouble(), p.GetProperty("h").GetDouble(), p.GetProperty("margin").GetDouble());
                if (!Visible)
                {
                    Show();
                    Activate();
                    SetForegroundWindow(Handle);
                    web.Focus();
                    outside.Start();
                    Send("shown", null);
                }
                break;

            case "invoke":
            {
                var id = p.GetProperty("id").GetString() ?? "";
                if (!byId.TryGetValue(id, out var item) || item.Disabled) return;
                string? value = p.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (item.KeepOpen)
                {
                    try { item.Run?.Invoke(value); }
                    catch (Exception ex) { Log.Error("Menu action failed", ex); }
                    if (build != null) Send("update", new { items = Index(build()) });
                }
                else
                {
                    HideMenu();
                    app.Post(() => item.Run?.Invoke(value));
                }
                break;
            }

            case "close":
                HideMenu();
                break;
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HideMenu();
    }

    /// Backup for when Windows doesn't let the menu take focus: any click outside closes it.
    void CloseIfClickedOutside()
    {
        if (!Visible) { outside.Stop(); return; }
        bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        if (down && !Bounds.Contains(Cursor.Position)) HideMenu();
    }

    void HideMenu()
    {
        outside.Stop();
        if (!Visible) return;
        Hide();
        Send("reset", null);
    }

    void Send(string ev, object? d)
    {
        if (web.CoreWebView2 == null) return;
        try { web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { ev, d }, Json.Web)); }
        catch (Exception ex) { Log.Warn($"Menu send {ev} failed: {ex.Message}"); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
}

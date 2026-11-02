using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static M0Spike.Native;

namespace M0Spike;

class WidgetForm : Form
{
    readonly Options o;
    readonly int index;
    readonly CoreWebView2Environment env;
    readonly WebView2 web;
    readonly System.Windows.Forms.Timer dragTimer = new() { Interval = 8 };
    POINT dragCursor0;
    Point dragPos0;

    bool Desktop => o.Z == "desktop";

    public WidgetForm(Options o, int index, CoreWebView2Environment env, Point pos)
    {
        this.o = o;
        this.index = index;
        this.env = env;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Location = pos;
        Text = $"widget{index}";

        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
        Controls.Add(web);
        dragTimer.Tick += DragTick;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (o is null) return cp; // called once from the base constructor
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            if (o.Z == "top") cp.ExStyle |= WS_EX_TOPMOST;
            if (o.ClickThrough == "layered") cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
            if (o.NoActivate) cp.ExStyle |= WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (o.ClickThrough == "layered") SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);

        var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(Handle, ref m);
        int dark = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);

        if (o.Backdrop == "none")
        {
            int corner = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            uint border = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, 4);
        }
        else if (o.Backdrop is "blur" or "accent")
        {
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            if (o.Backdrop == "blur") SetAccent(Handle, ACCENT_ENABLE_BLURBEHIND, 0);
            else SetAccent(Handle, ACCENT_ENABLE_ACRYLICBLURBEHIND, 0x40221A16); // ABGR tint
        }
        else
        {
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            int type = o.Backdrop == "mica" ? DWMSBT_MAINWINDOW : DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4);
        }

    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Desktop)
        {
            // Owned by the desktop window, so it stays visible on Win+D. Done after Show:
            // WinForms rewrites the owner (to its hidden taskbar owner) while showing.
            var progman = FindWindow("Progman", null);
            SetWindowLongPtr(Handle, GWLP_HWNDPARENT, progman);
            // Sit directly above the desktop, under every app window.
            var above = GetWindow(progman, GW_HWNDPREV);
            if (above != Handle)
                SetWindowPos(Handle, above, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Log.Write(new { ev = "desktopOwner", i = index, owner = (long)GetWindowLongPtr(Handle, GWLP_HWNDPARENT) });
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (Desktop && o.ZLock && m.Msg == WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(m.LParam);
            if ((wp.flags & SWP_NOZORDER) == 0)
            {
                wp.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(wp, m.LParam, false);
            }
        }
        base.WndProc(ref m);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        double scale = DeviceDpi / 96.0;
        bool card = o.Backdrop != "none";
        // Transparent mode leaves a 20px margin around the card for the CSS shadow.
        Size = card ? new Size((int)(320 * scale), (int)(200 * scale))
                    : new Size((int)(360 * scale), (int)(240 * scale));

        try
        {
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            if (o.Drag == "appregion") core.Settings.IsNonClientRegionSupportEnabled = true;
            if (o.MemLow) core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;

            string host = $"w{index}.widgets.local";
            core.SetVirtualHostNameToFolderMapping(host, Path.Combine(AppContext.BaseDirectory, "pages"),
                CoreWebView2HostResourceAccessKind.Allow);
            core.NewWindowRequested += (_, a) => a.Handled = true;
            core.WebMessageReceived += OnWebMessage;
            core.Navigate($"https://{host}/widget.html?i={index}&drag={o.Drag}&card={(card ? 1 : 0)}");

            Log.Write(new
            {
                ev = "window", i = index, hwnd = (long)Handle, x = Left, y = Top, w = Width, h = Height,
                dpi = DeviceDpi, browserPid = core.BrowserProcessId
            });
        }
        catch (Exception ex)
        {
            Log.Write(new { ev = "error", i = index, ex = ex.ToString() });
        }
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs a)
    {
        using var doc = JsonDocument.Parse(a.WebMessageAsJson);
        var type = doc.RootElement.GetProperty("type").GetString();
        switch (type)
        {
            case "ready":
                Log.Write(new { ev = "ready", i = index });
                break;
            case "drag":
                GetCursorPos(out dragCursor0);
                dragPos0 = Location;
                dragTimer.Start();
                break;
            case "nativeDrag":
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
                Log.Write(new { ev = "dragEnd", mode = "native", i = index, x = Left, y = Top });
                break;
        }
    }

    void DragTick(object? sender, EventArgs e)
    {
        GetCursorPos(out var p);
        Location = new Point(dragPos0.X + p.X - dragCursor0.X, dragPos0.Y + p.Y - dragCursor0.Y);
        if (!PrimaryButtonDown())
        {
            dragTimer.Stop();
            Log.Write(new { ev = "dragEnd", mode = "manual", i = index, x = Left, y = Top });
        }
    }
}

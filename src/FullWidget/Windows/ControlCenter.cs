using System.Text.Json;
using FullWidget.Bridge;
using FullWidget.Core;
using FullWidget.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static FullWidget.Native.Win32;

namespace FullWidget.Windows;

/// The control center: a normal app window whose UI is the page in ui/control-center.
/// It re-reads the whole state whenever anything changes (debounced), so it never goes stale,
/// whether the change came from itself, a widget, the tray menu or a command.
sealed class ControlCenter : Form
{
    const string Host = "app.fullwidget.local";
    static readonly Color Surface = Color.FromArgb(14, 16, 19);

    readonly App app;
    readonly ControlBridge bridge;
    readonly WebView2 web;
    readonly System.Windows.Forms.Timer pushTimer = new() { Interval = 120 };
    bool ready;
    PageWatch? watch;

    public ControlCenter(App app)
    {
        this.app = app;
        bridge = new ControlBridge(app, this);
        Text = $"{Strings.AppName} {Strings.ControlCenter}";
        Icon = AppIcon.Create();
        BackColor = Surface;
        StartPosition = FormStartPosition.Manual;
        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Surface };
        Controls.Add(web);
        pushTimer.Tick += (_, _) => { pushTimer.Stop(); Push(); };
        app.Config.Changed += OnConfigChanged;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        uint caption = (uint)(Surface.B << 16 | Surface.G << 8 | Surface.R); // COLORREF is 0x00BBGGRR
        DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref caption, 4);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        double s = DeviceDpi / 96.0;
        MinimumSize = new Size((int)(880 * s), (int)(580 * s));
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        var size = new Size(Math.Min((int)(1120 * s), wa.Width - 40), Math.Min((int)(740 * s), wa.Height - 40));
        Bounds = new Rectangle(wa.Left + (wa.Width - size.Width) / 2, wa.Top + (wa.Height - size.Height) / 2, size.Width, size.Height);

        try
        {
            await web.EnsureCoreWebView2Async(app.Env);
            var core = web.CoreWebView2;
            bool dev = app.Config.Data.DeveloperMode;
            core.Settings.AreDevToolsEnabled = dev;
            core.Settings.AreDefaultContextMenusEnabled = dev;
            core.Settings.AreBrowserAcceleratorKeysEnabled = dev;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Host, Paths.ControlCenterDir, CoreWebView2HostResourceAccessKind.Allow);
            // widget folders, for the preview pictures in the widget library
            core.SetVirtualHostNameToFolderMapping(Previews.BuiltinHost, Paths.BuiltinWidgetsDir, CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(Previews.UserHost, Paths.UserWidgetsDir, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += OnMessage;
            core.NewWindowRequested += (_, a) => a.Handled = true;
            core.NavigationStarting += (_, a) =>
            {
                if (!a.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) a.Cancel = true;
                else ready = false; // a reload: events wait for the page again
            };
            watch = new PageWatch("Control center", $"https://{Host}/", () => IsDisposed ? null : web.CoreWebView2);
            watch.Attach(core);
            core.Navigate($"https://{Host}/index.html");
        }
        catch (Exception ex)
        {
            Log.Error("Control center failed to load", ex);
        }
    }

    // Widgets set to "always on top" would otherwise cover this window's buttons. While the user
    // works here it stays above them; once another app is active it goes back to normal.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        TopMost = true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        TopMost = false;
    }

    public void Select(string instanceId) => Post("select", instanceId);

    public void ShowPage(string page) => Post("page", page);

    readonly List<(string ev, object? data)> queued = new();

    /// Sends an event to the page, or keeps it until the page has loaded.
    public void Post(string ev, object? data)
    {
        if (ready) Send(ev, data);
        else queued.Add((ev, data));
    }

    /// Lets the user pick a .fwz to install (Windows' own file dialog).
    public string? PickPackage()
    {
        using var d = new OpenFileDialog
        {
            Title = Strings.PickPackageTitle,
            Filter = Strings.PackageFilter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
        };
        return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
    }

    public string? PickExportFile(string fileName)
    {
        using var d = new SaveFileDialog
        {
            Title = Strings.ExportTitle,
            Filter = Strings.ExportFilter,
            FileName = fileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            OverwritePrompt = true,
        };
        return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
    }

    void OnConfigChanged()
    {
        if (!ready) return;
        pushTimer.Stop();
        pushTimer.Start();
    }

    void Push()
    {
        object state;
        try { state = bridge.State(); }
        catch (Exception ex)
        {
            Log.Error("Control center: building the state failed", ex);
            Send("stateError", ex.Message);
            return;
        }
        if (!Send("state", state)) Send("stateError", "state");
    }

    bool Send(string ev, object? data)
    {
        if (IsDisposed || web.CoreWebView2 == null) return false;
        try { web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { ev, d = data }, Json.Web)); return true; }
        catch (Exception ex) { Log.Error($"Control center: sending {ev} failed", ex); return false; }
    }

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try { msg = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson); }
        catch { return; }
        if (msg.ValueKind != JsonValueKind.Object || !msg.TryGetProperty("m", out var mEl) || mEl.ValueKind != JsonValueKind.String) return;
        string method = mEl.GetString()!;
        bool hasId = msg.TryGetProperty("id", out var id);
        var p = msg.TryGetProperty("p", out var pEl) ? pEl : default;

        if (method == "ready")
        {
            watch?.Ready();
            ready = true;
            Push();
            foreach (var (ev, data) in queued) Send(ev, data);
            queued.Clear();
            return;
        }
        if (method == "package.drop")
        {
            // Files dropped on the page arrive as objects that carry their path.
            foreach (var f in e.AdditionalObjects?.OfType<CoreWebView2File>() ?? Enumerable.Empty<CoreWebView2File>())
                app.Post(() => app.OpenPackage(f.Path));
            return;
        }
        try
        {
            var result = await bridge.HandleAsync(method, p);
            if (hasId) Reply(new { id, r = result });
        }
        catch (Exception ex)
        {
            if (ex is not BridgeException) Log.Error($"Control center: {method} failed", ex);
            if (hasId) Reply(new { id, e = ex.Message });
        }
    }

    void Reply(object payload)
    {
        if (IsDisposed || web.CoreWebView2 == null) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json.Web));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        app.Config.Changed -= OnConfigChanged;
        pushTimer.Dispose();
        watch?.Dispose();
        base.OnFormClosed(e);
    }
}

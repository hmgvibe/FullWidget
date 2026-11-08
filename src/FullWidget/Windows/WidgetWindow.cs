using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FullWidget.Bridge;
using FullWidget.Core;
using Microsoft.Web.WebView2.Core;
using static FullWidget.Native.Win32;

namespace FullWidget.Windows;

/// One widget on the desktop: a borderless, per-pixel transparent native window hosting a
/// WebView2 controller. See docs/M0-技術驗證報告.md for why each window trick is used.
sealed class WidgetWindow : NativeWindow
{
    static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

    readonly App app;
    CoreWebView2Controller? controller;
    bool closing, hidden, pageReady;
    int dpi = 96;
    int marginDip;

    readonly System.Windows.Forms.Timer resizeTimer = new() { Interval = 10 };
    POINT resizeCursor0;
    // where a move started: the window and the cursor (see Snap)
    POINT moveCursor0;
    POINT? dragGrab; // where the pointer went down, for a drag the page started
    RECT moveRect0;
    Size resizeSize0;

    public InstanceConfig Cfg { get; }
    public WidgetManifest Manifest { get; }
    public CoreWebView2? Web { get; private set; }

    /// The declared permissions the user has allowed (all of them for built-in widgets).
    public List<string> Allowed => Permissions.Allowed(app.Config.Data, Manifest);

    /// Raised when the window was destroyed by someone else (e.g. Explorer restarting).
    public event Action<WidgetWindow>? Lost;

    public WidgetWindow(App app, InstanceConfig cfg, WidgetManifest manifest)
    {
        this.app = app;
        Cfg = cfg;
        Manifest = manifest;
        resizeTimer.Tick += ResizeTick;
    }

    string Origin => "https://" + Manifest.HostName;
    double Scale => dpi / 96.0;
    bool DesktopLayer => Cfg.Layer is not ("top" or "normal");
    int MarginPx => (int)Math.Round(marginDip * Scale);

    // ───────────────────────── creation & teardown ─────────────────────────

    public void Create()
    {
        var look = Appearance.Effective(app.Config.Data, Cfg);
        marginDip = Appearance.MarginDip(look);
        dpi = DpiAt(CardX, CardY);
        var r = OuterRect();

        CreateHandle(new CreateParams
        {
            Caption = "FullWidget: " + Manifest.Id,
            X = r.X, Y = r.Y, Width = r.Width, Height = r.Height,
            Style = WS_POPUP | WS_CLIPCHILDREN,
            ExStyle = WS_EX_TOOLWINDOW | (Cfg.Layer == "top" ? WS_EX_TOPMOST : 0),
            // Owned by the desktop window, so it survives Win+D.
            Parent = DesktopLayer ? FindWindow("Progman", null) : IntPtr.Zero,
        });
        dpi = (int)GetDpiForWindow(Handle);

        ApplyFrame(look);
        ApplyClickThrough();
        hidden = userHidden = app.Config.Data.HideAll;
        if (!hidden) ShowNoActivate();
        _ = InitWebAsync();
    }

    public void Close()
    {
        if (closing) return;
        closing = true;
        resizeTimer.Dispose();
        app.Monitor.Unsubscribe(this);
        app.Audio.Unsubscribe(this);
        app.Media.Unsubscribe(this);
        try { controller?.Close(); } catch { /* already gone */ }
        controller = null;
        Web = null;
        if (Handle != IntPtr.Zero) DestroyHandle();
    }

    async Task InitWebAsync()
    {
        try
        {
            var c = await app.Env!.CreateCoreWebView2ControllerAsync(Handle);
            if (closing || Handle == IntPtr.Zero) { c.Close(); return; }
            controller = c;
            c.DefaultBackgroundColor = Color.Transparent;
            UpdateWebBounds();
            c.IsVisible = !hidden;

            var web = Web = c.CoreWebView2;
            bool dev = app.Config.Data.DeveloperMode;
            var s = web.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = dev;
            s.AreBrowserAcceleratorKeysEnabled = dev;   // F5 / F12 only for developers
            s.IsBuiltInErrorPageEnabled = dev;
            s.IsStatusBarEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsPinchZoomEnabled = false;
            s.IsSwipeNavigationEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            if (app.Config.Data.LowResourceMode) web.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;

            web.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += OnResourceRequested;
            web.WebMessageReceived += OnWebMessage;
            web.NavigationStarting += OnNavigationStarting;
            web.NewWindowRequested += (_, e) => e.Handled = true;
            web.ProcessFailed += OnProcessFailed;
            web.Navigate(Origin + "/" + Manifest.Entry.Replace('\\', '/'));
        }
        catch (Exception ex)
        {
            Log.Error($"WebView init failed for {Cfg.Id} ({Manifest.Id})", ex);
        }
    }

    // ───────────────────────── geometry ─────────────────────────

    /// Where the card shows while the monitor it belongs on is missing. Its saved place (Cfg.X/Y)
    /// is kept, so it goes back there when that monitor returns. Null when it is at home.
    public Point? Away { get; private set; }
    int CardX => Away?.X ?? Cfg.X;
    int CardY => Away?.Y ?? Cfg.Y;

    /// Shows the card at a stand-in spot (or back at its saved place, with null) without saving.
    public void ShowAt(Point? away)
    {
        Away = away;
        if (Handle != IntPtr.Zero) ApplyOuterRect();
    }

    /// Window rectangle in physical pixels: the card plus the shadow margin around it.
    Rectangle OuterRect()
    {
        int m = MarginPx;
        return new Rectangle(CardX - m, CardY - m,
            (int)Math.Round(Cfg.Width * Scale) + 2 * m,
            (int)Math.Round(Cfg.Height * Scale) + 2 * m);
    }

    /// The visible card in physical pixels.
    public Rectangle CardRect() =>
        new(CardX, CardY, (int)Math.Round(Cfg.Width * Scale), (int)Math.Round(Cfg.Height * Scale));

    void ApplyOuterRect()
    {
        var r = OuterRect();
        SetWindowPos(Handle, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    void SaveBounds()
    {
        if (!GetWindowRect(Handle, out var r)) return;
        int m = MarginPx;
        Away = null; // placed by hand: this is its home now
        Cfg.X = r.Left + m;
        Cfg.Y = r.Top + m;
        Cfg.Width = Math.Round((r.Right - r.Left - 2 * m) / Scale, 1);
        Cfg.Height = Math.Round((r.Bottom - r.Top - 2 * m) / Scale, 1);
        app.Config.SaveSoon();
        if (app.EditMode) Send("state", StatePayload()); // refresh the size label
    }

    /// While dragging, snaps the card to nearby lines (see SnapLayout) and shows what it lined up
    /// with. Hold Ctrl to place freely (Alt would cancel the Windows move loop).
    void Snap(ref RECT r)
    {
        // Windows proposes each new position from the last one, which was already snapped, so a
        // slow drag would stay stuck on a line. Work from where the move started instead.
        GetCursorPos(out var p);
        int dx = p.X - moveCursor0.X, dy = p.Y - moveCursor0.Y;
        r = new RECT { Left = moveRect0.Left + dx, Top = moveRect0.Top + dy, Right = moveRect0.Right + dx, Bottom = moveRect0.Bottom + dy };
        int m = MarginPx;
        var card = Rectangle.FromLTRB(r.Left + m, r.Top + m, r.Right - m, r.Bottom - m);
        card = SnapCard(card, SnapLayout.Edges.None);
        r = new RECT { Left = card.Left - m, Top = card.Top - m, Right = card.Right + m, Bottom = card.Bottom + m };
    }

    /// Snaps a card rectangle (physical pixels) that is being moved or resized, and shows the guides.
    Rectangle SnapCard(Rectangle card, SnapLayout.Edges resizing)
    {
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) { SnapGuides.Hide(); return card; }
        var s = Manifest.Size;
        var (result, guides) = SnapLayout.Snap(card, Screen.FromRectangle(card).WorkingArea, app.CardRects(this).ToList(),
            gap: Px(12), reach: Px(8), resizing,
            min: new Size(Px(s.MinWidth), Px(s.MinHeight)), max: new Size(Px(s.MaxWidth), Px(s.MaxHeight)));
        SnapGuides.Show(guides, GuideInk(), Math.Max(1, Px(1)), Px(5), Px(4));
        return result;
    }

    int Px(double dip) => (int)Math.Round(dip * Scale);

    /// The guides use this widget's accent colour.
    Color GuideInk()
    {
        var look = Appearance.Effective(app.Config.Data, Cfg);
        try { if (look.TryGetValue("accentColor", out var c) && c.ValueKind == JsonValueKind.String) return ColorTranslator.FromHtml(c.GetString()!); }
        catch (Exception) { }
        return Color.FromArgb(0x4c, 0xc9, 0xf0);
    }

    void UpdateWebBounds()
    {
        if (controller == null) return;
        GetClientRect(Handle, out var rc);
        controller.Bounds = new Rectangle(0, 0, rc.Right - rc.Left, rc.Bottom - rc.Top);
    }

    public object SizeInfo() => new { width = Cfg.Width, height = Cfg.Height };

    public void SetSizeDip(double w, double h)
    {
        var s = Manifest.Size;
        Cfg.Width = Math.Clamp(w, s.MinWidth, s.MaxWidth);
        Cfg.Height = Math.Clamp(h, s.MinHeight, s.MaxHeight);
        ApplyOuterRect();
        app.Config.SaveSoon();
    }

    public void MoveCardTo(int x, int y)
    {
        Away = null;
        Cfg.X = x;
        Cfg.Y = y;
        ApplyOuterRect();
        app.Config.SaveSoon();
    }

    // ───────────────────────── look & behaviour ─────────────────────────

    void ApplyFrame(Dictionary<string, JsonElement> look)
    {
        // Extending the frame over the whole window gives per-pixel alpha: what the web page
        // leaves transparent shows the desktop.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(Handle, ref margins);
        int dark = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        uint border = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, 4);

        bool frosted = Appearance.Style(look) == "frosted";
        int corner = frosted ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
        if (frosted)
        {
            if (!SetAccent(Handle, ACCENT_ENABLE_ACRYLICBLURBEHIND, Appearance.FrostedTint(look)))
                Log.Warn("Frosted style not supported on this system");
            wasFrosted = true;
        }
        else if (wasFrosted)
        {
            // Turning the blur off also turns off per-pixel transparency (the window goes black).
            // Re-apply the frame: reset the margins, extend again, and make DWM redo the frame.
            SetAccent(Handle, ACCENT_DISABLED, 0);
            var none = new MARGINS();
            DwmExtendFrameIntoClientArea(Handle, ref none);
            DwmExtendFrameIntoClientArea(Handle, ref margins);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            wasFrosted = false;
        }
    }

    bool wasFrosted;

    /// Re-reads appearance (after a theme or instance change) and updates window and page.
    public void ApplyAppearance()
    {
        var look = Appearance.Effective(app.Config.Data, Cfg);
        int newMargin = Appearance.MarginDip(look);
        ApplyFrame(look);
        if (newMargin != marginDip)
        {
            marginDip = newMargin;
            ApplyOuterRect(); // card stays where it is; only the margin around it changes
            ApplyClickThrough();
        }
        Send("appearance", AppearancePayload());
    }

    void ApplyClickThrough()
    {
        // Layered whenever clicks may need to pass through: always for click-through widgets, and
        // for widgets with a shadow margin, whose invisible margin lets clicks through (see
        // SetMarginPassThrough). Frosted widgets have no margin and stay non-layered.
        bool layered = ClickThroughNow || marginDip > 0;
        bool transparent = ClickThroughNow || (marginDip > 0 && overMargin);
        long ex = GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64();
        long next = (layered ? ex | WS_EX_LAYERED : ex & ~WS_EX_LAYERED);
        next = transparent ? next | WS_EX_TRANSPARENT : next & ~WS_EX_TRANSPARENT;
        if (next == ex) return;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(next));
        if (layered && (ex & WS_EX_LAYERED) == 0) SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
    }

    bool overMargin;

    /// Called by the app's cursor watcher: while the pointer is over the invisible shadow margin,
    /// clicks go to whatever is behind the widget.
    public void SetMarginPassThrough(bool over)
    {
        if (overMargin == over || closing) return;
        overMargin = over;
        ApplyClickThrough();
    }

    /// The window rectangle including the shadow margin, in physical pixels.
    public Rectangle WindowRect()
    {
        GetWindowRect(Handle, out var r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public bool HasMargin => marginDip > 0 && !hidden && !closing;

    // In arrange mode every widget can be moved and clicked, whatever its own settings say.
    bool LockedNow => Cfg.Locked && !app.EditMode;
    bool ClickThroughNow => Cfg.ClickThrough && !app.EditMode;

    object StatePayload() => new
    {
        locked = LockedNow,
        layer = Cfg.Layer,
        clickThrough = Cfg.ClickThrough,
        edit = app.EditMode,
        label = $"{Manifest.DisplayName}  {Cfg.Width:0} × {Cfg.Height:0}",
    };

    public void ApplyState()
    {
        ApplyLayer();
        ApplyClickThrough();
        Send("state", StatePayload());
    }

    /// "Locate": lifts the widget above everything for a moment and makes it pulse.
    public void Identify()
    {
        if (closing || hidden) return;
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Send("identify", new { name = Manifest.DisplayName });
        var t = new System.Windows.Forms.Timer { Interval = 1800 };
        t.Tick += (_, _) => { t.Dispose(); if (!closing) ApplyLayer(); };
        t.Start();
    }

    /// When this widget last had the keyboard (Environment.TickCount64), 0 if never.
    public long ActivatedAt { get; private set; }

    bool peeking;
    IntPtr peekFrom; // the window that had the keyboard before
    /// Called up with a shortcut: above everything and holding the keyboard.
    public bool Peeking => peeking;

    /// The notes shortcut: lifts the widget above every window (showing it even if widgets are
    /// hidden), gives it the keyboard and tells the page, so it can focus its text box. It goes
    /// back to its own layer once another window is used or the shortcut is pressed again.
    public void Peek(bool on, bool giveBack = false)
    {
        if (peeking == on || closing) return;
        peeking = on;
        if (on)
        {
            peekFrom = GetForegroundWindow();
            ApplyVisibility();
            SetWindowLongPtr(Handle, GWLP_HWNDPARENT, IntPtr.Zero); // a desktop-layer owner would hold it down
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(Handle); // allowed: the shortcut's key press belongs to this app
            // a window that was just sent back down doesn't take "topmost" until it's active again
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
            Send("summon", null);
        }
        else
        {
            ApplyLayer();
            ApplyVisibility();
            // put away with the shortcut: typing goes back to where it was
            if (giveBack && peekFrom != IntPtr.Zero && peekFrom != Handle && IsWindow(peekFrom)) SetForegroundWindow(peekFrom);
            peekFrom = IntPtr.Zero;
        }
    }

    void ApplyLayer()
    {
        if (peeking) return;
        const uint keep = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;
        if (DesktopLayer)
        {
            SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0, keep);
            SetWindowLongPtr(Handle, GWLP_HWNDPARENT, FindWindow("Progman", null));
            PlaceAboveDesktop();
        }
        else
        {
            SetWindowLongPtr(Handle, GWLP_HWNDPARENT, IntPtr.Zero);
            SetWindowPos(Handle, Cfg.Layer == "top" ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, keep);
            if (Cfg.Layer == "normal") SetWindowPos(Handle, HWND_TOP, 0, 0, 0, 0, keep);
        }
    }

    /// Puts the window directly above the desktop: under every app window.
    void PlaceAboveDesktop()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return;
        var above = GetWindow(progman, GW_HWNDPREV);
        if (above == IntPtr.Zero || above == Handle) return;
        SetWindowPos(Handle, above, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    void ShowNoActivate()
    {
        ShowWindow(Handle, SW_SHOWNOACTIVATE);
        if (DesktopLayer && !peeking) PlaceAboveDesktop();
    }

    bool userHidden, paused;

    /// "Hide all widgets".
    public void SetHidden(bool hide)
    {
        userHidden = hide;
        ApplyVisibility();
    }

    /// A full-screen app covers this widget's monitor: hide it and stop its page until it's gone.
    public void SetPaused(bool pause)
    {
        if (paused == pause) return;
        paused = pause;
        ApplyVisibility();
    }

    /// Hidden for any reason: such widgets get no system updates.
    public bool IsHidden => hidden;

    void ApplyVisibility()
    {
        bool hide = (userHidden || paused) && !peeking;
        if (hidden == hide || closing) return;
        hidden = hide;
        if (hide)
        {
            ShowWindow(Handle, SW_HIDE);
            if (controller != null) controller.IsVisible = false;
            _ = Web?.TrySuspendAsync();
        }
        else
        {
            if (Web?.IsSuspended == true) Web.Resume();
            if (controller != null) controller.IsVisible = true;
            ShowNoActivate();
        }
    }

    public void Reload()
    {
        pageReady = false;
        Web?.Reload();
    }

    public void OpenDevTools() => Web?.OpenDevToolsWindow();

    // ───────────────────────── drag & resize ─────────────────────────

    /// dx, dy: how far the pointer moved before the page asked to drag (screen pixels).
    public void BeginDrag(double dx = 0, double dy = 0)
    {
        if (LockedNow || closing) return;
        GetCursorPos(out var now);
        dragGrab = Math.Abs(dx) < 200 && Math.Abs(dy) < 200
            ? new POINT { X = now.X - (int)Math.Round(dx), Y = now.Y - (int)Math.Round(dy) } : now;
        // Posted so the web message handler returns before the modal move loop starts.
        app.Post(() =>
        {
            if (closing || !PrimaryButtonDown()) { dragGrab = null; return; }
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
        });
    }

    public void BeginResize()
    {
        if (LockedNow || closing || !Manifest.Size.Resizable) return;
        GetCursorPos(out resizeCursor0);
        GetWindowRect(Handle, out var r);
        resizeSize0 = new Size(r.Right - r.Left, r.Bottom - r.Top);
        resizeTimer.Start();
    }

    void ResizeTick(object? sender, EventArgs e)
    {
        GetCursorPos(out var p);
        var s = Manifest.Size;
        int m2 = 2 * MarginPx;
        int w = Math.Clamp(resizeSize0.Width + p.X - resizeCursor0.X, (int)(s.MinWidth * Scale) + m2, (int)(s.MaxWidth * Scale) + m2);
        int h = Math.Clamp(resizeSize0.Height + p.Y - resizeCursor0.Y, (int)(s.MinHeight * Scale) + m2, (int)(s.MaxHeight * Scale) + m2);
        // the grip drags the right and bottom edges; they snap like a moving card does
        GetWindowRect(Handle, out var r);
        int m = MarginPx;
        var card = SnapCard(new Rectangle(r.Left + m, r.Top + m, w - m2, h - m2), SnapLayout.Edges.Right | SnapLayout.Edges.Bottom);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, card.Width + m2, card.Height + m2, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        if (!PrimaryButtonDown())
        {
            resizeTimer.Stop();
            SnapGuides.Hide();
            SaveBounds();
        }
    }

    // ───────────────────────── window messages ─────────────────────────

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND:
                // Black = fully transparent on the extended DWM frame.
                GetClientRect(Handle, out var rc);
                FillRect(m.WParam, ref rc, GetStockObject(BLACK_BRUSH));
                m.Result = 1;
                return;

            case WM_SIZE:
                UpdateWebBounds();
                break;

            case WM_MOVE:
                controller?.NotifyParentWindowPositionChanged();
                break;

            case WM_ACTIVATE:
                // A click activates the widget (so menus and typing work), which also lifts it
                // to the top. Put a desktop-layer widget straight back under the app windows.
                // (Blocking the lift up front doesn't work: the click lands in the WebView's
                // window, which belongs to another process and never forwards WM_MOUSEACTIVATE.)
                if ((m.WParam.ToInt64() & 0xFFFF) != WA_INACTIVE) ActivatedAt = Environment.TickCount64;
                if (peeking && (m.WParam.ToInt64() & 0xFFFF) == WA_INACTIVE)
                    app.Post(() => Peek(false)); // another window was used: back to its layer
                else if (DesktopLayer && !peeking && (m.WParam.ToInt64() & 0xFFFF) != WA_INACTIVE)
                    app.Post(() => { if (!closing && DesktopLayer && !peeking) PlaceAboveDesktop(); });
                break;

            case WM_GETMINMAXINFO:
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
                var s = Manifest.Size;
                int m2 = 2 * MarginPx;
                mmi.MinTrackSize = new POINT { X = (int)(s.MinWidth * Scale) + m2, Y = (int)(s.MinHeight * Scale) + m2 };
                mmi.MaxTrackSize = new POINT { X = (int)(s.MaxWidth * Scale) + m2, Y = (int)(s.MaxHeight * Scale) + m2 };
                Marshal.StructureToPtr(mmi, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }

            case WM_ENTERSIZEMOVE:
                if (dragGrab is { } g) moveCursor0 = g; else GetCursorPos(out moveCursor0);
                dragGrab = null;
                GetWindowRect(Handle, out moveRect0);
                break;

            case WM_EXITSIZEMOVE:
                SnapGuides.Hide();
                SaveBounds();
                break;

            case WM_MOVING:
            {
                var r = Marshal.PtrToStructure<RECT>(m.LParam);
                Snap(ref r);
                Marshal.StructureToPtr(r, m.LParam, false);
                m.Result = 1;
                return;
            }

            case WM_DPICHANGED:
            {
                dpi = (int)(m.WParam.ToInt64() & 0xFFFF);
                var r = Marshal.PtrToStructure<RECT>(m.LParam);
                SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SWP_NOZORDER | SWP_NOACTIVATE);
                m.Result = IntPtr.Zero;
                return;
            }

            case WM_SETFOCUS:
                controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
                break;

            case WM_CLOSE:
                if (!closing) { m.Result = IntPtr.Zero; return; } // ignore Alt+F4
                break;

            case WM_NCDESTROY:
                base.WndProc(ref m);
                if (!closing)
                {
                    closing = true;
                    Log.Warn($"Widget window {Cfg.Id} was destroyed externally");
                    app.Post(() => Lost?.Invoke(this));
                }
                return;
        }

        if ((uint)m.Msg == WM_TASKBARCREATED && DesktopLayer)
            app.Post(ApplyLayer); // Explorer restarted: re-attach to the new desktop window

        base.WndProc(ref m);
    }

    // ───────────────────────── web content ─────────────────────────

    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith(Origin + "/", StringComparison.OrdinalIgnoreCase)) { pageReady = false; return; }
        e.Cancel = true;
        Log.Warn($"[{Manifest.Id}] blocked navigation to {e.Uri}");
    }

    void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Warn($"[{Manifest.Id}] WebView process failed: {e.ProcessFailedKind} ({e.Reason})");
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            var t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += (_, _) => { t.Dispose(); if (!closing) Reload(); };
            t.Start();
        }
    }

    /// Serves the widget's own files plus the SDK under /__fw/, from disk, on the widget's origin.
    void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = app.Env!;
        try
        {
            var uri = new Uri(e.Request.Uri);
            if (!uri.Host.Equals(Manifest.HostName, StringComparison.OrdinalIgnoreCase)) return;
            if (e.Request.Method is not ("GET" or "HEAD"))
            {
                e.Response = env.CreateWebResourceResponse(new MemoryStream(), 405, "Method Not Allowed", "");
                return;
            }

            var rel = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            if (rel.StartsWith("__media/cover/", StringComparison.Ordinal)) { ServeCover(e, rel["__media/cover/".Length..]); return; }
            if (rel.StartsWith("__files/", StringComparison.Ordinal)) { ServeFile(e, rel["__files/".Length..], uri.Query); return; }
            // /__dw/ is the SDK folder's name from before the rename; widgets may still ask for it.
            bool sdk = rel.StartsWith("__fw/", StringComparison.Ordinal) || rel.StartsWith("__dw/", StringComparison.Ordinal);
            string root = Path.GetFullPath(sdk ? Paths.SdkDir : Manifest.Dir);
            if (sdk) rel = rel["__fw/".Length..];
            else if (rel.Length == 0) rel = Manifest.Entry;

            var file = Path.GetFullPath(Path.Combine(root, rel));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            {
                // The browser asks every page for a favicon; widgets don't need one.
                if (rel.Equals("favicon.ico", StringComparison.OrdinalIgnoreCase))
                {
                    e.Response = env.CreateWebResourceResponse(new MemoryStream(), 204, "No Content", "");
                    return;
                }
                e.Response = env.CreateWebResourceResponse(new MemoryStream(), 404, "Not Found", "Content-Type: text/plain");
                return;
            }

            byte[] bytes = File.ReadAllBytes(file);
            if (!sdk && file.Equals(Path.GetFullPath(Path.Combine(Manifest.Dir, Manifest.Entry)), StringComparison.OrdinalIgnoreCase))
                bytes = Encoding.UTF8.GetBytes(InjectHost(DecodeUtf8(bytes)));

            string mime = Mime.For(file);
            string headers = $"Content-Type: {mime}\nCache-Control: no-cache";
            // Pages may only reach the network hosts the user allowed (images, scripts, fetch…).
            if (mime.StartsWith("text/html", StringComparison.Ordinal))
                headers += "\nContent-Security-Policy: " + Permissions.ContentSecurityPolicy(Allowed);
            e.Response = env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", headers);
        }
        catch (Exception ex)
        {
            Log.Error($"[{Manifest.Id}] resource {e.Request.Uri}", ex);
            e.Response = env.CreateWebResourceResponse(new MemoryStream(), 500, "Internal Error", "");
        }
    }

    /// The current track's album art, for widgets with the "media" permission.
    void ServeCover(CoreWebView2WebResourceRequestedEventArgs e, string key)
    {
        var env = app.Env!;
        if (Permissions.Has(Allowed, "media") && app.Media.Cover(key) is { } c)
            e.Response = env.CreateWebResourceResponse(new MemoryStream(c.bytes), 200, "OK", $"Content-Type: {c.mime}\nCache-Control: max-age=86400");
        else e.Response = env.CreateWebResourceResponse(new MemoryStream(), 404, "Not Found", "");
    }

    static readonly Regex WidthParam = new(@"[?&]w=(\d{1,5})", RegexOptions.Compiled);

    /// A picture from a folder the user chose: /__files/&lt;setting&gt;/&lt;path&gt;, or a
    /// smaller copy with ?w=&lt;pixels&gt; (much less memory than decoding a 24-megapixel photo).
    void ServeFile(CoreWebView2WebResourceRequestedEventArgs e, string rest, string query)
    {
        var env = app.Env!;
        int slash = rest.IndexOf('/');
        string? full = null;
        if (slash > 0)
            try { full = Providers.FolderFiles.Resolve(Providers.FolderFiles.Root(this, app, rest[..slash]), rest[(slash + 1)..]); }
            catch (Exception ex) when (ex is BridgeException or IOException or ArgumentException or UnauthorizedAccessException) { }
        if (full == null)
        {
            e.Response = env.CreateWebResourceResponse(new MemoryStream(), 404, "Not Found", "");
            return;
        }
        const string cache = "Cache-Control: max-age=31536000"; // the widget's URLs carry the file's time
        var m = WidthParam.Match(query);
        string ext = Path.GetExtension(full).ToLowerInvariant();
        bool browserCanShow = ext is ".jpg" or ".jpeg" or ".jfif" or ".png" or ".gif" or ".webp" or ".bmp" or ".avif";
        if ((m.Success && ext != ".gif") || !browserCanShow)
        {
            int w = m.Success ? int.Parse(m.Groups[1].Value) : 1600;
            _ = ServeThumbnailAsync(e, full, Math.Clamp((w + 255) / 256 * 256, 256, 2560));
            return;
        }
        try
        {
            var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            e.Response = env.CreateWebResourceResponse(fs, 200, "OK", $"Content-Type: {Mime.For(full)}\n{cache}");
        }
        catch (IOException) { e.Response = env.CreateWebResourceResponse(new MemoryStream(), 404, "Not Found", ""); }
    }

    async Task ServeThumbnailAsync(CoreWebView2WebResourceRequestedEventArgs e, string full, int size)
    {
        var env = app.Env!;
        var deferral = e.GetDeferral();
        try
        {
            var r = await Task.Run(() => Providers.FolderFiles.Thumbnail(full, size));
            e.Response = r is { } x
                ? env.CreateWebResourceResponse(new MemoryStream(x.bytes), 200, "OK", $"Content-Type: {x.mime}\nCache-Control: max-age=31536000")
                : env.CreateWebResourceResponse(new MemoryStream(), 415, "Unsupported Media Type", "");
        }
        catch (Exception ex)
        {
            Log.Warn($"[{Manifest.Id}] picture {full}: {ex.Message}");
            try { e.Response = env.CreateWebResourceResponse(new MemoryStream(), 500, "Internal Error", ""); } catch { /* page gone */ }
        }
        finally { try { deferral.Complete(); } catch { /* page gone */ } }
    }

    static string DecodeUtf8(byte[] b) =>
        b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? Encoding.UTF8.GetString(b, 3, b.Length - 3) : Encoding.UTF8.GetString(b);

    static readonly Regex HeadOpen = new(@"<head\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex HtmlOpen = new(@"<html\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// Adds base CSS, theme variables, boot data and the SDK at the top of &lt;head&gt;, and the
    /// instance's own overrides at the end, so they beat the widget's CSS.
    string InjectHost(string html)
    {
        var a = AppearancePayload();
        string boot = JsonSerializer.Serialize(BootData(a), Json.Web);
        string top =
            "<link rel=\"stylesheet\" href=\"/__fw/base.css\">" +
            $"<link id=\"__fw_theme_link\" rel=\"stylesheet\" href=\"{a.themeHref}\">" +
            $"<style id=\"__fw_theme\">{a.themeCss}</style>" +
            $"<script>window.__FW_BOOT__={boot};</script>" +
            "<script src=\"/__fw/widget-sdk.js\"></script>";
        string bottom = $"<style id=\"__fw_user\">{a.userCss}</style>";

        var head = HeadOpen.Match(html);
        if (head.Success) html = html.Insert(head.Index + head.Length, top);
        else
        {
            var root = HtmlOpen.Match(html);
            html = html.Insert(root.Success ? root.Index + root.Length : 0, "<head>" + top + "</head>");
        }
        int close = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return close >= 0 ? html.Insert(close, bottom) : html + bottom;
    }

    sealed record AppearanceInfo(string theme, string themeHref, string style, bool shadow, int margin,
                                 string themeCss, string userCss, Dictionary<string, JsonElement> values);

    AppearanceInfo AppearancePayload()
    {
        var look = Appearance.Effective(app.Config.Data, Cfg);
        var (themeCss, userCss) = Appearance.PageCss(app.Config.Data, Cfg);
        var theme = Appearance.ThemeOf(app.Config.Data, Cfg);
        return new AppearanceInfo(theme.Id, theme.CssHref, Appearance.Style(look), Appearance.Shadow(look),
            Appearance.MarginDip(look), themeCss, userCss, look);
    }

    object BootData(AppearanceInfo a) => new
    {
        hostVersion = App.Version,
        instanceId = Cfg.Id,
        widgetId = Manifest.Id,
        widgetVersion = Manifest.Version,
        locale = Text.Locale,
        settings = EffectiveSettings(),
        appearance = a,
        state = StatePayload(),
        locked = LockedNow,
        layer = Cfg.Layer,
        clickThrough = Cfg.ClickThrough,
        resizable = Manifest.Size.Resizable,
        size = new { width = Manifest.Size.Width, height = Manifest.Size.Height, grow = Math.Clamp(Manifest.Size.Grow, 0, 1) },
        developer = app.Config.Data.DeveloperMode,
        permissions = Allowed,
    };

    /// Manifest defaults overlaid with what the user changed.
    public Dictionary<string, JsonElement> EffectiveSettings()
    {
        var d = new Dictionary<string, JsonElement>();
        foreach (var s in Manifest.Settings)
        {
            if (Cfg.Settings.TryGetValue(s.Key, out var v)) d[s.Key] = v;
            // Defaults may be given per language: { "zh-TW": "待辦", "en": "To do" }.
            else if (s.Default is { } def) d[s.Key] = Text.PickValue(def);
            else d[s.Key] = Json.Element<object?>(null);
            // "%USERPROFILE%\Pictures" → the real folder
            if (s.Type == "folder" && d[s.Key].ValueKind == JsonValueKind.String)
                d[s.Key] = Json.Element(Environment.ExpandEnvironmentVariables(d[s.Key].GetString() ?? ""));
        }
        return d;
    }

    public void OnPageReady()
    {
        pageReady = true;
        // called up before the page had loaded (a note just added by the shortcut)
        if (peeking) { controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic); Send("summon", null); }
    }

    /// Pushes an event to the page: { ev, d }.
    public void Send(string ev, object? data)
    {
        if (!pageReady || closing || Web == null) return;
        try { Web.PostWebMessageAsJson(JsonSerializer.Serialize(new { ev, d = data }, Json.Web)); }
        catch (Exception ex) { Log.Warn($"[{Manifest.Id}] send {ev} failed: {ex.Message}"); }
    }

    async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Origin + "/", StringComparison.OrdinalIgnoreCase)) return;
        JsonElement msg;
        try { msg = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson); }
        catch { return; }
        if (msg.ValueKind != JsonValueKind.Object || !msg.TryGetProperty("m", out var mEl) || mEl.ValueKind != JsonValueKind.String)
            return;

        bool hasId = msg.TryGetProperty("id", out var id);
        var p = msg.TryGetProperty("p", out var pEl) ? pEl : default;
        try
        {
            // files dropped on the page come along as objects carrying their paths
            var files = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).ToList();
            var result = await app.Bridge.HandleAsync(this, mEl.GetString()!, p, files);
            if (hasId) Reply(new { id, r = result });
        }
        catch (Exception ex)
        {
            if (ex is not BridgeException) Log.Error($"[{Manifest.Id}] {mEl.GetString()} failed", ex);
            if (hasId) Reply(new { id, e = ex.Message });
        }
    }

    void Reply(object payload)
    {
        if (closing || Web == null) return;
        try { Web.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json.Web)); }
        catch (Exception ex) { Log.Warn($"[{Manifest.Id}] reply failed: {ex.Message}"); }
    }
}

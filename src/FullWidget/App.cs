using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FullWidget.Bridge;
using FullWidget.Core;
using FullWidget.Native;
using FullWidget.Providers;
using FullWidget.Shell;
using FullWidget.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace FullWidget;

/// The running application: owns config, the WebView2 environment, every widget window and the tray.
sealed class App : ApplicationContext
{
    public static readonly string Version = typeof(App).Assembly.GetName().Version!.ToString(3);

    public ConfigStore Config { get; } = new();
    public Catalog Catalog { get; } = new();
    public StorageStore Storage { get; } = new();
    public SystemMonitor Monitor { get; } = new();
    public Notifier Notifier { get; }
    public AudioVolume Audio { get; }
    public MediaSession Media { get; }
    public Market Market { get; }
    public Updater Updater { get; }
    /// Gives unused memory back to Windows after start and after big changes (Shell/MemoryTrim.cs).
    public MemoryTrim Trim { get; }
    public WidgetBridge Bridge { get; }
    public CoreWebView2Environment? Env { get; private set; }
    /// System-wide shortcuts; null until start-up has finished.
    public Hotkeys? Keys { get; private set; }

    readonly Dictionary<string, WidgetWindow> windows = new();
    readonly SynchronizationContext ui;
    readonly TrayIcon tray;
    public TrayIcon Tray => tray;
    readonly string? dataDirArg;
    /// The --data-dir this copy was started with, if any.
    public string? DataDirArg => dataDirArg;
    bool exiting;

    public App(string pipeName, string? dataDirArg, string? startupCommand)
    {
        ui = SynchronizationContext.Current ?? throw new InvalidOperationException("no UI context");
        this.dataDirArg = dataDirArg;
        AutoStart.DataDirArg = dataDirArg;
        Bridge = new WidgetBridge(this);
        tray = new TrayIcon(this);
        Notifier = new Notifier(tray.Notify);
        Audio = new AudioVolume(this);
        Media = new MediaSession(this);
        Market = new Market(this);
        Updater = new Updater(this);
        Trim = new MemoryTrim(() => Env);
        menuIdle.Tick += (_, _) => ReleaseMenu();
        CommandChannel.Listen(pipeName, cmd => Post(() => Execute(cmd)));
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        displaySettle.Tick += (_, _) => { displaySettle.Stop(); PlaceAll(); };
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        marginWatch.Tick += (_, _) => WatchMargins();
        marginWatch.Start();
        fullscreenWatch.Tick += (_, _) => WatchFullscreen();
        fullscreenWatch.Start();
        _ = StartAsync(startupCommand);
    }

    // The invisible shadow margin around a card must not swallow clicks meant for what's behind.
    // Checked only when the mouse has moved, which costs next to nothing.
    readonly System.Windows.Forms.Timer marginWatch = new() { Interval = 30 };
    /// Places widgets again once monitors have settled after a change.
    readonly System.Windows.Forms.Timer displaySettle = new() { Interval = 2000 };
    Point lastCursor = new(int.MinValue, int.MinValue);

    void WatchMargins()
    {
        Win32.GetCursorPos(out var c);
        var p = new Point(c.X, c.Y);
        if (p == lastCursor) return;
        lastCursor = p;
        foreach (var w in windows.Values)
        {
            if (!w.HasMargin) continue;
            w.SetMarginPassThrough(w.WindowRect().Contains(p) && !w.CardRect().Contains(p));
        }
    }

    // ── full-screen pause ──
    // A game, video or slideshow filling a monitor: widgets there would either sit on top of it
    // (top layer) or keep drawing unseen underneath. Either way they hide and stop until it ends.
    readonly System.Windows.Forms.Timer fullscreenWatch = new() { Interval = 1500 };
    Rectangle? fullscreen;
    static readonly HashSet<string> ShellClasses = new() { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    void WatchFullscreen()
    {
        Rectangle? now = Config.Data.PauseOnFullscreen ? FullscreenMonitor() : null;
        if (now == fullscreen) return;
        fullscreen = now;
        Log.Info(now is { } r ? $"Full-screen app on {r}; pausing widgets there" : "Full-screen app gone; widgets back");
        foreach (var w in windows.Values) w.SetPaused(now is { } m && m.Contains(Center(w.CardRect())));
    }

    static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    /// The monitor covered by the foreground window, if that window is another app's full-screen one.
    static Rectangle? FullscreenMonitor()
    {
        var fg = Win32.GetForegroundWindow();
        if (fg == IntPtr.Zero || !Win32.IsWindowVisible(fg) || Win32.IsIconic(fg)) return null;
        // A maximized window or one with a title bar is ordinary work, even when an auto-hiding
        // taskbar lets it cover the whole screen.
        const long WS_CAPTION = 0x00C00000;
        if (Win32.IsZoomed(fg) || (Win32.GetWindowLongPtr(fg, -16).ToInt64() & WS_CAPTION) == WS_CAPTION) return null;
        Win32.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == Environment.ProcessId) return null;
        var cls = new System.Text.StringBuilder(64);
        Win32.GetClassName(fg, cls, 64);
        if (ShellClasses.Contains(cls.ToString())) return null; // the desktop itself covers the screen too
        if (!Win32.GetWindowRect(fg, out var wr)) return null;
        var rect = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
        var screen = Screen.FromHandle(fg).Bounds;
        return rect.Contains(screen) ? screen : null;
    }

    public void SetPauseOnFullscreen(bool on)
    {
        Config.Data.PauseOnFullscreen = on;
        Config.SaveSoon();
        WatchFullscreen();
    }

    /// All widget windows, in config order.
    public IEnumerable<WidgetWindow> Windows =>
        Config.Data.Instances.Select(i => windows.GetValueOrDefault(i.Id)).OfType<WidgetWindow>();

    /// Runs an action on the UI thread, after the current message is handled.
    public void Post(Action action) => ui.Post(_ =>
    {
        try { action(); }
        catch (Exception ex) { Log.Error("UI callback failed", ex); }
    }, null);

    async Task StartAsync(string? startupCommand)
    {
        try
        {
            Config.Load();
            ApplyLanguage();
            Themes.Load();
            Packages.CleanStaging();
            AutoStart.Repair();
            Catalog.Scan();
            // Test profiles (--data-dir) never claim the file type for themselves.
            if (Config.Data.FileAssociation && dataDirArg == null) FileAssociation.Register(null);

            try { CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(Strings.NoWebView2, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitThread();
                return;
            }

            // One environment for all widgets. --process-per-site lets every widget share one
            // renderer process, since they all live under *.widgets.local.
            LowResourceActive = Config.Data.LowResourceMode;
            var args = "--process-per-site" + (LowResourceActive ? " --disable-gpu" : "");
            Env = await CoreWebView2Environment.CreateAsync(null, Paths.WebViewDataDir, new CoreWebView2EnvironmentOptions(args));
            Env.BrowserProcessExited += (_, e) =>
            {
                if (!exiting) Log.Warn($"WebView2 browser process exited: {e.BrowserProcessExitKind}");
            };
            Log.Info($"WebView2 {Env.BrowserVersionString}, args: {args}");

            if (!Config.Data.FirstRunDone)
            {
                CreateDefaultLayout();
                Config.Data.FirstRunDone = true;
                Config.SaveSoon();
                tray.Balloon(Strings.FirstRun);
            }
            foreach (var inst in Config.Data.Instances.ToList()) Open(inst);
            Log.Info($"Started with {windows.Count} widget(s)");
            Keys = new Hotkeys(RunShortcut);
            Keys.Apply(Config.Data.Hotkeys);

            if (Config.Data.DeveloperMode) StartHotReload();

            Trim.Soon();

            Updater.Start();
            if (!string.IsNullOrEmpty(startupCommand)) Execute(startupCommand);
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            MessageBox.Show(string.Format(Strings.StartFailed, ex.Message), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitThread();
        }
    }

    void CreateDefaultLayout()
    {
        foreach (var id in new[] { "fullwidget.clock", "fullwidget.weather", "fullwidget.sysinfo" })
            if (Catalog.Find(id) is { } m) AddInstance(m);
    }

    // ───────────────────────── instances ─────────────────────────

    WidgetWindow? Open(InstanceConfig inst)
    {
        var m = Catalog.Find(inst.Widget);
        if (m == null)
        {
            Log.Warn(string.Format(Strings.WidgetMissing, inst.Widget));
            return null;
        }
        if (inst.Width <= 0) inst.Width = m.Size.Width;
        if (inst.Height <= 0) inst.Height = m.Size.Height;
        var w = new WidgetWindow(this, inst, m);
        w.ShowAt(StandIn(inst, null));
        w.Lost += OnWindowLost;
        windows[inst.Id] = w;
        w.Create();
        if (fullscreen is { } f && f.Contains(Center(w.CardRect()))) w.SetPaused(true);
        Trim.Soon();
        return w;
    }

    InstanceConfig AddInstance(WidgetManifest m)
    {
        var inst = new InstanceConfig
        {
            Widget = m.Id,
            Width = m.Size.Width,
            Height = m.Size.Height,
            Layer = m.Layer is "top" or "normal" ? m.Layer : "desktop",
        };
        var spot = FindFreeSpot(inst.Width, inst.Height);
        inst.X = spot.X;
        inst.Y = spot.Y;
        Config.Data.Instances.Add(inst);
        return inst;
    }

    /// Adding from a menu: a widget that still needs the user's OK for its permissions is shown
    /// in the control center first, where the user can read what it asks for.
    public void RequestAdd(WidgetManifest m)
    {
        if (Permissions.Pending(Config.Data, m).Count == 0) { AddWidget(m); return; }
        OpenControlCenter(page: "add");
        controlCenter?.Post("consent", new { widget = m.Id, then = "add" });
    }

    /// The user allowed what the widget declares. Running copies reload to pick it up.
    public void Grant(WidgetManifest m)
    {
        Permissions.Grant(Config.Data, m);
        Config.SaveSoon();
        Log.Info($"Allowed {m.Id}: {string.Join(", ", Config.Data.Grants[m.Id])}");
        foreach (var w in Windows.Where(w => w.Manifest.Id == m.Id)) w.Reload();
    }

    public InstanceConfig AddWidget(WidgetManifest m)
    {
        if (Config.Data.HideAll) SetHideAll(false);
        var inst = AddInstance(m);
        Open(inst);
        Config.SaveSoon();
        Log.Info($"Added {m.Id} as {inst.Id}");
        return inst;
    }

    /// A copy with the same settings, look and size, placed in a free spot. Stored data is not copied.
    public InstanceConfig Duplicate(InstanceConfig src)
    {
        var copy = new InstanceConfig
        {
            Widget = src.Widget,
            Width = src.Width,
            Height = src.Height,
            Layer = src.Layer,
            ThemeId = src.ThemeId,
            CustomCss = src.CustomCss,
            Settings = src.Settings.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
            Appearance = src.Appearance.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        };
        var spot = FindFreeSpot(copy.Width, copy.Height);
        copy.X = spot.X;
        copy.Y = spot.Y;
        Config.Data.Instances.Add(copy);
        Open(copy);
        Config.SaveSoon();
        return copy;
    }

    /// Removes an instance, including one whose widget is no longer installed.
    public void Remove(InstanceConfig inst)
    {
        if (windows.TryGetValue(inst.Id, out var w)) { w.Close(); windows.Remove(inst.Id); }
        Notifier.Cancel(inst.Id, null);
        Config.Data.Instances.Remove(inst);
        Storage.DeleteInstance(inst.Id);
        Config.SaveSoon();
        Trim.Soon();
        Log.Info($"Removed {inst.Id}");
    }

    public WidgetWindow? WindowOf(string instanceId) => windows.GetValueOrDefault(instanceId);

    /// Visible cards of all other widgets, for snapping.
    public IEnumerable<Rectangle> CardRects(WidgetWindow except) =>
        windows.Values.Where(w => w != except && !Config.Data.HideAll).Select(w => w.CardRect());

    // ───────────────────────── arrange mode & control center ─────────────────────────

    /// Arrange mode: every widget shows an outline and can be moved, even if locked or click-through.
    public bool EditMode { get; private set; }

    public void SetEditMode(bool on)
    {
        if (EditMode == on) return;
        EditMode = on;
        foreach (var w in Windows) w.ApplyState();
        Config.SaveSoon(); // nothing to save, but tells listeners the state changed
    }

    ControlCenter? controlCenter;

    /// Opens (or brings back) the control center, optionally on one widget or page ("settings").
    public void OpenControlCenter(string? instanceId = null, string? page = null)
    {
        if (controlCenter == null || controlCenter.IsDisposed)
        {
            controlCenter = new ControlCenter(this);
            controlCenter.FormClosed += (_, _) => { controlCenter = null; SetEditMode(false); Trim.Soon(); };
            controlCenter.Show();
        }
        else
        {
            if (controlCenter.WindowState == FormWindowState.Minimized) controlCenter.WindowState = FormWindowState.Normal;
            controlCenter.Activate();
        }
        if (instanceId != null) controlCenter.Select(instanceId);
        else if (page != null) controlCenter.ShowPage(page);
    }

    static string[]? fontNames;
    public static string[] FontNames => fontNames ??= new System.Drawing.Text.InstalledFontCollection().Families
        .Select(f => f.Name).Where(n => !n.StartsWith('@')).Distinct().OrderBy(n => n, StringComparer.CurrentCulture).ToArray();

    void OnWindowLost(WidgetWindow w)
    {
        windows.Remove(w.Cfg.Id);
        if (exiting) return;
        // Typically Explorer restarted and took the desktop window with it. Retry shortly.
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            if (!exiting && Config.Data.Instances.Contains(w.Cfg) && !windows.ContainsKey(w.Cfg.Id)) Open(w.Cfg);
        };
        timer.Start();
    }

    // ───────────────────────── menus ─────────────────────────

    // The menu's page is made when a menu is wanted (or the mouse rests on the tray icon), and
    // let go after three idle minutes: kept around, it costs a renderer process (~18 MB) for
    // something used now and then. Made fresh it opens in about 0.3 s instead of 0.04 s.
    MenuPopup? menu;
    readonly System.Windows.Forms.Timer menuIdle = new() { Interval = 180_000 };

    public void ShowMenu(Func<List<PopItem>> build, Point at, bool fromTray)
    {
        if (Env == null) { Log.Warn("Menu not ready yet"); return; }
        menuIdle.Stop();
        WarmMenu().Open(build, at, fromTray);
    }

    /// The menu window, made now if it isn't there; it is let go again after three idle minutes.
    public MenuPopup WarmMenu()
    {
        if (menu == null || menu.IsDisposed)
        {
            var m = menu = new MenuPopup(this);
            m.Closed += () => { if (menu == m) { menuIdle.Stop(); menuIdle.Start(); } };
            _ = InitMenuAsync(m);
            menuIdle.Stop();
            menuIdle.Start();
        }
        return menu;
    }

    async Task InitMenuAsync(MenuPopup m)
    {
        try { await m.InitAsync(); }
        catch (Exception ex) { Log.Error("Menu: could not create its page", ex); if (menu == m) menu = null; m.Dispose(); }
    }

    void ReleaseMenu()
    {
        menuIdle.Stop();
        if (menu == null || menu.Visible) return;
        menu.Dispose();
        menu = null;
        Trim.Soon();
    }

    public void ShowInstanceMenu(WidgetWindow w) => ShowMenu(() => Menus.ForInstance(this, w), Cursor.Position, fromTray: false);

    // ───────────────────────── per-instance changes ─────────────────────────

    public void SetLocked(WidgetWindow w, bool locked)
    {
        w.Cfg.Locked = locked;
        w.ApplyState();
        Config.SaveSoon();
    }

    public void SetLayer(WidgetWindow w, string layer)
    {
        w.Cfg.Layer = layer is "top" or "normal" ? layer : "desktop";
        w.ApplyState();
        Config.SaveSoon();
    }

    public void SetClickThrough(WidgetWindow w, bool on)
    {
        w.Cfg.ClickThrough = on;
        w.ApplyState();
        Config.SaveSoon();
        if (on) tray.Balloon(string.Format(Strings.ClickThroughTip, w.Manifest.DisplayName));
    }

    /// null removes the override, so the theme's value applies again.
    public void SetAppearance(WidgetWindow w, string key, object? value)
    {
        if (!Appearance.Defaults.ContainsKey(key)) throw new BridgeException($"unknown appearance key '{key}'");
        if (value == null) w.Cfg.Appearance.Remove(key);
        else w.Cfg.Appearance[key] = value is JsonElement je ? je.Clone() : Json.Element(value);
        w.ApplyAppearance();
        Config.SaveSoon();
    }

    public void SetCustomCss(WidgetWindow w, string? css)
    {
        w.Cfg.CustomCss = string.IsNullOrWhiteSpace(css) ? null : css;
        w.ApplyAppearance();
        Config.SaveSoon();
    }

    /// The user's global tweaks on top of the theme; null removes one.
    public void SetThemeOverride(string key, JsonElement? value)
    {
        if (!Appearance.Defaults.ContainsKey(key)) throw new BridgeException($"unknown appearance key '{key}'");
        if (value == null) Config.Data.Theme.Remove(key);
        else Config.Data.Theme[key] = value.Value.Clone();
        foreach (var w in Windows) w.ApplyAppearance();
        Config.SaveSoon();
    }

    public void ResetThemeOverrides()
    {
        Config.Data.Theme.Clear();
        foreach (var w in Windows) w.ApplyAppearance();
        Config.SaveSoon();
    }

    public void SetSetting(WidgetWindow w, string key, JsonElement value)
    {
        if (w.Manifest.Settings.All(s => s.Key != key))
            throw new BridgeException($"unknown setting '{key}' (declare it under settings in widget.json)");
        w.Cfg.Settings[key] = value.Clone();
        Config.SaveSoon();
        w.Send("settings", w.EffectiveSettings());
    }

    /// Switches every widget that follows the global theme. The user's global tweaks belong to
    /// the old theme, so they are dropped.
    public void SetGlobalTheme(string id)
    {
        Config.Data.ThemeId = Themes.Get(id).Id;
        Config.Data.Theme.Clear();
        foreach (var w in Windows) w.ApplyAppearance();
        Config.SaveSoon();
    }

    /// null = follow the global theme.
    public void SetInstanceTheme(WidgetWindow w, string? id)
    {
        w.Cfg.ThemeId = id == null ? null : Themes.Get(id).Id;
        w.ApplyAppearance();
        Config.SaveSoon();
    }

    /// Widgets, menus, the control center and messages all follow one language.
    void ApplyLanguage()
    {
        Text.Locale = Config.Data.Language == "auto" ? CultureInfo.CurrentUICulture.Name : Config.Data.Language;
        Strings.SetLanguage(Text.Locale);
    }

    /// "auto", "zh-TW", "zh-CN" or "en". Widgets reload to pick it up.
    public void SetLanguage(string lang)
    {
        Config.Data.Language = lang;
        Config.SaveNow();
        ApplyLanguage();
        Catalog.Scan(); // load errors are written in the UI language
        foreach (var w in Windows) w.Reload();
        if (controlCenter is { IsDisposed: false }) controlCenter.Text = $"{Strings.AppName} {Strings.ControlCenter}";
        if (docs is { IsDisposed: false }) docs.Close(); // the guide comes in another language
    }

    public void SetAllLocked(bool locked)
    {
        foreach (var w in Windows) { w.Cfg.Locked = locked; w.ApplyState(); }
        Config.SaveSoon();
    }

    // ───────────────────────── shortcuts ─────────────────────────

    void RunShortcut(string action)
    {
        if (action == Hotkeys.ToggleAll) SetHideAll(!Config.Data.HideAll);
        else if (action == Hotkeys.Notes) ToggleNotes();
    }

    const string NotesWidget = "fullwidget.notes";

    /// The notes shortcut: the note used last comes above everything, ready to type in; pressed
    /// again it goes back. With no note on the desktop yet, one is added.
    void ToggleNotes()
    {
        var notes = Windows.Where(w => w.Manifest.Id.Equals(NotesWidget, StringComparison.OrdinalIgnoreCase)).ToList();
        if (notes.FirstOrDefault(w => w.Peeking) is { } up) { up.Peek(false, giveBack: true); return; }
        if (notes.Count == 0)
        {
            if (Catalog.Find(NotesWidget) is not { } m) return;
            var inst = AddInstance(m);
            Config.SaveSoon();
            Log.Info($"Added {NotesWidget} as {inst.Id} for the notes shortcut");
            if (Open(inst) is not { } w) return;
            notes.Add(w);
        }
        notes.MaxBy(w => w.ActivatedAt)!.Peek(true);
    }

    /// Changes one shortcut; "" turns it off.
    public void SetShortcut(string action, string keys)
    {
        if (!Hotkeys.Actions.Contains(action)) throw new BridgeException($"unknown shortcut '{action}'");
        keys = keys.Trim();
        if (keys.Length > 0 && !Hotkeys.TryParse(keys, out _, out _)) throw new BridgeException(Strings.ShortcutInvalid);
        var other = Config.Data.Hotkeys.FirstOrDefault(kv => kv.Key != action && kv.Value.Equals(keys, StringComparison.OrdinalIgnoreCase));
        if (keys.Length > 0 && other.Key != null) throw new BridgeException(Strings.ShortcutInUse);
        Config.Data.Hotkeys[action] = keys;
        Keys?.Apply(Config.Data.Hotkeys);
        Config.SaveSoon();
    }

    public void SetHideAll(bool hide)
    {
        Config.Data.HideAll = hide;
        foreach (var w in Windows) w.SetHidden(hide);
        Config.SaveSoon();
    }

    public void RescanWidgets()
    {
        Catalog.Scan();
        foreach (var w in windows.Values.ToList())
        {
            // Pick up edited manifests by reopening; plain file edits only need a reload.
            w.Close();
            windows.Remove(w.Cfg.Id);
        }
        foreach (var inst in Config.Data.Instances) Open(inst);
    }

    /// Closes and reopens the running copies of one widget, e.g. after its widget.json changed.
    void Reopen(string widgetId)
    {
        foreach (var w in windows.Values.Where(w => w.Manifest.Id.Equals(widgetId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            w.Close();
            windows.Remove(w.Cfg.Id);
        }
        foreach (var inst in Config.Data.Instances.Where(i => i.Widget.Equals(widgetId, StringComparison.OrdinalIgnoreCase) && !windows.ContainsKey(i.Id)))
            Open(inst);
    }

    public void ToggleDeveloperMode() => SetDeveloperMode(!Config.Data.DeveloperMode);

    public void SetDeveloperMode(bool on)
    {
        if (Config.Data.DeveloperMode == on) return;
        Config.Data.DeveloperMode = on;
        Config.SaveNow();
        if (on) StartHotReload(); else StopHotReload();
        RescanWidgets(); // re-creates the web views with the new settings
    }

    // ───────────────────────── developer: reload on save ─────────────────────────

    HotReload? hotReload;

    void StartHotReload()
    {
        StopHotReload();
        var roots = new List<string> { Paths.UserWidgetsDir };
        if (Paths.DevRoot) roots.AddRange(new[] { Paths.BuiltinWidgetsDir, Paths.SdkDir }); // working on FullWidget itself
        hotReload = new HotReload(roots, OnFilesChanged);
    }

    void StopHotReload()
    {
        hotReload?.Dispose();
        hotReload = null;
    }

    /// A saved file reloads the widgets that use it; a changed widget.json (or a new or removed
    /// widget folder) re-reads the catalog first.
    void OnFilesChanged(IReadOnlyCollection<string> paths)
    {
        var sdk = Path.GetFullPath(Paths.SdkDir) + Path.DirectorySeparatorChar;
        bool sdkChanged = false, rescan = false;
        var manifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (path.StartsWith(sdk, StringComparison.OrdinalIgnoreCase)) { sdkChanged = true; continue; }
            var m = Catalog.All.FirstOrDefault(x => path.StartsWith(x.Dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                                  || path.Equals(x.Dir, StringComparison.OrdinalIgnoreCase));
            if (m == null) { rescan = true; continue; } // a new folder, or one that didn't load before
            if (path.Equals(m.Dir, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("widget.json", StringComparison.OrdinalIgnoreCase))
                manifests.Add(m.Id);
            else files.Add(m.Id);
        }

        if (rescan || manifests.Count > 0)
        {
            var before = Catalog.All.ToDictionary(m => m.Id, m => m, StringComparer.OrdinalIgnoreCase);
            Catalog.Scan();
            // reopen what changed, appeared or went away
            foreach (var m in Catalog.All)
                if (!before.TryGetValue(m.Id, out var old) || old.Dir != m.Dir || manifests.Contains(m.Id)) manifests.Add(m.Id);
            foreach (var id in before.Keys.Where(id => Catalog.Find(id) == null)) manifests.Add(id);
            foreach (var id in manifests) Reopen(id);
        }
        if (sdkChanged)
        {
            Themes.Load();
            foreach (var w in Windows) { w.ApplyAppearance(); w.Reload(); }
        }
        else
        {
            foreach (var w in Windows.Where(w => files.Contains(w.Manifest.Id) && !manifests.Contains(w.Manifest.Id))) w.Reload();
        }
        Log.Info($"Hot reload: {paths.Count} change(s); reloaded {string.Join(", ", files.Concat(manifests).Distinct())}{(sdkChanged ? ", sdk" : "")}");
        Config.Notify();
    }

    /// "新建組件": a new widget from the template, placed on the desktop. Developer mode turns on,
    /// so saving a file reloads the widget, and the folder opens in Explorer.
    public (InstanceConfig inst, string dir) CreateWidget(string name)
    {
        hotReload?.Mute(Paths.UserWidgetsDir); // before writing: the watcher reports from other threads
        var (id, dir) = Scaffold.Create(name, Catalog);
        Config.Data.OwnWidgets.Add(id);
        SetDeveloperMode(true);
        Catalog.Scan();
        var m = Catalog.Find(id) ?? throw new InvalidDataException(Strings.NotLoadedSeeLog);
        var inst = AddWidget(m);
        Process.Start("explorer.exe", $"\"{dir}\"");
        return (inst, dir);
    }

    // ───────────────────────── packages (.fwz) ─────────────────────────

    readonly Dictionary<string, PendingPackage> pendingPackages = new();

    /// Opens a package for installing: unpacks it and shows what it is and what it may do in the
    /// control center. Nothing is installed until the user says so there.
    /// `from`: what to show as its source instead of the file name (e.g. "from the widget market").
    public void OpenPackage(string file, string? from = null)
    {
        OpenControlCenter(page: "add");
        try
        {
            var p = Packages.Open(file);
            pendingPackages[p.Token] = p;
            var m = p.Manifest;
            var existing = Catalog.Find(m.Id);
            var before = existing == null ? new List<string>() : Permissions.Allowed(Config.Data, existing);
            controlCenter?.Post("install", new
            {
                token = p.Token,
                file = from ?? Path.GetFileName(file),
                id = m.Id,
                name = m.DisplayName,
                description = Text.Pick(m.Description),
                version = m.Version,
                author = m.Author,
                icon = Icons.For(m),
                permissions = m.Permissions.Where(Permissions.IsKnown).Distinct(StringComparer.OrdinalIgnoreCase),
                unknown = m.Permissions.Where(x => !Permissions.IsKnown(x)),
                newPermissions = m.Permissions.Where(x => Permissions.IsKnown(x) && !Permissions.Has(before, x)),
                update = existing == null ? null : new
                {
                    from = existing.Version,
                    count = Config.Data.Instances.Count(i => i.Widget == m.Id),
                    // "newer", "same" (reinstall) or "older" (going back)
                    kind = Packages.CompareVersions(m.Version, existing.Version) switch { > 0 => "newer", 0 => "same", _ => "older" },
                },
            });
        }
        catch (InvalidDataException ex)
        {
            Log.Warn($"Package {file} refused: {ex.Message}");
            controlCenter?.Post("installError", new { file = Path.GetFileName(file), message = ex.Message });
        }
    }

    /// The user said yes: install (or update), allow its permissions, and put a new widget on the desktop.
    public object InstallPackage(string token)
    {
        if (!pendingPackages.Remove(token, out var p)) throw new BridgeException(Strings.AlreadyHandled);
        bool update = Catalog.Find(p.Manifest.Id) != null;
        foreach (var w in windows.Values.Where(w => w.Manifest.Id == p.Manifest.Id).ToList()) { w.Close(); windows.Remove(w.Cfg.Id); }
        hotReload?.Mute(Paths.UserWidgetsDir);
        try { Packages.Install(p, Catalog); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Packages.Cancel(p);
            Reopen(p.Manifest.Id);
            throw new BridgeException(string.Format(Strings.InstallFailed, ex.Message));
        }
        Catalog.Scan();
        var m = Catalog.Find(p.Manifest.Id) ?? throw new BridgeException(Strings.NotLoadedSeeLog);
        Permissions.Grant(Config.Data, m);
        Reopen(m.Id);
        string? instanceId = update ? null : AddWidget(m).Id;
        Config.SaveSoon();
        return new { id = m.Id, name = m.DisplayName, version = m.Version, update, instanceId };
    }

    public void CancelPackage(string token)
    {
        if (pendingPackages.Remove(token, out var p)) Packages.Cancel(p);
    }

    /// Removes a third-party widget: its copies on the desktop, their saved data, and its folder
    /// (to the Recycle Bin).
    public void Uninstall(WidgetManifest m)
    {
        if (m.Builtin) throw new BridgeException(Strings.BuiltinCantUninstall);
        foreach (var inst in Config.Data.Instances.Where(i => i.Widget == m.Id).ToList()) Remove(inst);
        hotReload?.Mute(m.Dir);
        Packages.Recycle(m.Dir);
        Config.Data.Grants.Remove(m.Id);
        Config.Data.OwnWidgets.RemoveAll(x => x.Equals(m.Id, StringComparison.OrdinalIgnoreCase));
        Catalog.Scan();
        Config.SaveSoon();
        Log.Info($"Uninstalled {m.Id}");
    }

    public void SetFileAssociation(bool on)
    {
        Config.Data.FileAssociation = on;
        if (on) FileAssociation.Register(dataDirArg); else FileAssociation.Unregister();
        Config.SaveSoon();
    }

    public bool FileAssociationActive => FileAssociation.IsRegistered(dataDirArg);

    DocsWindow? docs;

    /// The developer guide, in its own window.
    public void OpenDeveloperGuide()
    {
        if (!File.Exists(Paths.DeveloperGuide)) throw new BridgeException(Strings.GuideMissing);
        if (docs == null || docs.IsDisposed)
        {
            docs = new DocsWindow(this);
            docs.FormClosed += (_, _) => { docs = null; Trim.Soon(); };
            docs.Show();
        }
        else
        {
            if (docs.WindowState == FormWindowState.Minimized) docs.WindowState = FormWindowState.Normal;
            docs.Activate();
        }
    }

    public void ToggleLowResource()
    {
        SetLowResource(!Config.Data.LowResourceMode);
        if (MessageBox.Show(Strings.RestartNeeded, Strings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Restart();
    }

    /// Takes effect after a restart.
    public void SetLowResource(bool on)
    {
        Config.Data.LowResourceMode = on;
        Config.SaveNow();
    }

    /// Whether the running WebView2 was started in low-resource mode.
    public bool LowResourceActive { get; private set; }

    public void Restart()
    {
        var args = $"--wait-pid={Environment.ProcessId}" + (dataDirArg != null ? $" \"--data-dir={dataDirArg}\"" : "");
        Process.Start(Environment.ProcessPath!, args);
        ExitThread();
    }

    // ───────────────────────── placement ─────────────────────────

    /// Picks a spot on the primary screen that doesn't overlap other widgets, filling columns
    /// from the top-right corner.
    Point FindFreeSpot(double widthDip, double heightDip)
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        double scale = Win32.DpiAt(wa.X + 1, wa.Y + 1) / 96.0;
        int w = (int)(widthDip * scale), h = (int)(heightDip * scale);
        int gap = (int)(20 * scale), step = (int)(20 * scale);
        // open widgets count where they show now (a stand-in spot, if their monitor is missing)
        var taken = Config.Data.Instances.Select(i =>
        {
            if (windows.TryGetValue(i.Id, out var open)) return Rectangle.Inflate(open.CardRect(), gap, gap);
            double s = Win32.DpiAt(i.X, i.Y) / 96.0;
            return Rectangle.Inflate(new Rectangle(i.X, i.Y, (int)(i.Width * s), (int)(i.Height * s)), gap, gap);
        }).ToList();

        for (int x = wa.Right - gap - w; x >= wa.Left + gap; x -= step)
            for (int y = wa.Top + gap; y + h <= wa.Bottom - gap; y += step)
            {
                var r = new Rectangle(x, y, w, h);
                if (!taken.Any(t => t.IntersectsWith(r))) return r.Location;
            }
        return new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
    }

    /// Whether a card at (x, y) of this size would be usefully visible on some monitor now.
    static bool OnSomeScreen(int x, int y, double widthDip, double heightDip)
    {
        double scale = Win32.DpiAt(x, y) / 96.0;
        var card = new Rectangle(x, y, (int)(widthDip * scale), (int)(heightDip * scale));
        return Screen.AllScreens.Any(s =>
        {
            var i = Rectangle.Intersect(s.WorkingArea, card);
            return i.Width >= 40 && i.Height >= 40;
        });
    }

    /// Where a widget should show when the monitor it was placed on is missing (turned off,
    /// unplugged): null while its own place is visible. The saved place is never overwritten, so
    /// the widget goes back once that monitor returns. A spot it already stands in is kept.
    Point? StandIn(InstanceConfig inst, Point? current)
    {
        if (OnSomeScreen(inst.X, inst.Y, inst.Width, inst.Height)) return null;
        if (current is { } c && OnSomeScreen(c.X, c.Y, inst.Width, inst.Height)) return c;
        var spot = FindFreeSpot(inst.Width, inst.Height);
        Log.Info($"Instance {inst.Id}: its monitor at ({inst.X},{inst.Y}) is missing; showing it at ({spot.X},{spot.Y}) for now");
        return spot;
    }

    /// Monitors came or went. Windows also shuffles windows around by itself then, so every
    /// widget is put back where it belongs. Monitors waking up can take a moment to settle, so
    /// this runs again a little later.
    void OnDisplayChanged(object? sender, EventArgs e) => Post(() =>
    {
        PlaceAll();
        displaySettle.Stop();
        displaySettle.Start();
    });

    void PlaceAll()
    {
        foreach (var w in Windows.ToList())
        {
            var was = w.Away;
            var now = StandIn(w.Cfg, was);
            if (was != null && now == null) Log.Info($"Instance {w.Cfg.Id}: back on its own monitor");
            w.ShowAt(now);
        }
    }

    void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        Post(() =>
        {
            var accent = Themes.SystemAccent;
            Themes.RefreshSystemAccent();
            if (accent != Themes.SystemAccent)
                foreach (var w in Windows) w.ApplyAppearance();
        });
    }

    // ───────────────────────── command line ─────────────────────────

    WidgetWindow? FindWindow(string idOrWidget) =>
        windows.GetValueOrDefault(idOrWidget) ?? Windows.FirstOrDefault(w => w.Manifest.Id == idOrWidget);

    /// Commands from `FullWidget.exe --cmd "..."`; several may be joined with ';'.
    void Execute(string line)
    {
        // A file path may contain spaces and ';', so "install" takes the whole rest of the line.
        if (line.StartsWith("install ", StringComparison.Ordinal))
        {
            Log.Info($"Command: {line}");
            OpenPackage(line["install ".Length..].Trim().Trim('"'));
            return;
        }
        if (line.Contains(';'))
        {
            foreach (var part in line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Execute(part);
            return;
        }
        Log.Info($"Command: {line}");
        var a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (a.Length == 0) return;
        string Arg(int i) => i < a.Length ? a[i] : throw new ArgumentException($"'{a[0]}' needs more arguments");
        WidgetWindow Target() => FindWindow(Arg(1)) ?? throw new ArgumentException($"no widget '{Arg(1)}'");
        WidgetManifest Widget() => Catalog.Find(Arg(1)) ?? throw new ArgumentException($"widget '{Arg(1)}' not installed");
        try
        {
            switch (a[0])
            {
                case "activate": tray.Balloon(Strings.AlreadyRunning); break;
                case "exit": ExitThread(); break;
                case "add": AddWidget(Catalog.Find(Arg(1)) ?? throw new ArgumentException($"widget '{Arg(1)}' not installed")); break;
                case "remove": Remove(Target().Cfg); break;
                case "control": OpenControlCenter(a.Length > 1 ? Target().Cfg.Id : null); break;
                case "menu": // for testing: open a menu at a point; "menu tray x y" or "menu <widget> x y"; "menu close"
                {
                    if (Arg(1) == "close") { menu?.Dismiss(); break; }
                    var at = new Point(int.Parse(Arg(2)), int.Parse(Arg(3)));
                    if (Arg(1) == "tray") ShowMenu(() => Menus.ForTray(this), at, fromTray: true);
                    else { var w = Target(); ShowMenu(() => Menus.ForInstance(this, w), at, fromTray: false); }
                    break;
                }
                case "edit": SetEditMode(Arg(1) == "on"); break;
                case "identify": Target().Identify(); break;
                case "dump-state": // for UI development: the control center's state as JSON
                    File.WriteAllText(Arg(1), System.Text.Json.JsonSerializer.Serialize(new ControlBridge(this).State(), Json.Web));
                    break;
                case "lyrics": // for testing: "lyrics <out.json> <app>|<title>|<artist>|<ms>" looks a song up
                {
                    var q = string.Join(' ', a.Skip(2)).Split('|');
                    string outFile = Arg(1);
                    _ = Task.Run(async () =>
                    {
                        var r = await Providers.Lyrics.FindAsync(q[0], q[1], q.Length > 2 ? q[2] : "", "", q.Length > 3 ? long.Parse(q[3]) : 0);
                        File.WriteAllText(outFile, System.Text.Json.JsonSerializer.Serialize(r, Json.Web));
                    });
                    break;
                }
                case "rescan": RescanWidgets(); break;
                case "reload": Target().Reload(); break;
                case "lock": SetLocked(Target(), Arg(2) == "on"); break;
                case "layer": SetLayer(Target(), Arg(2)); break;
                case "clickthrough": SetClickThrough(Target(), Arg(2) == "on"); break;
                case "style": SetAppearance(Target(), "style", Arg(2)); break;
                case "shadow": SetAppearance(Target(), "shadow", Arg(2) == "on"); break;
                case "move": Target().MoveCardTo(int.Parse(Arg(2)), int.Parse(Arg(3))); break;
                case "size": Target().SetSizeDip(double.Parse(Arg(2), CultureInfo.InvariantCulture), double.Parse(Arg(3), CultureInfo.InvariantCulture)); break;
                case "lock-all": SetAllLocked(true); break;
                case "unlock-all": SetAllLocked(false); break;
                case "hide-all": SetHideAll(true); break;
                case "show-all": SetHideAll(false); break;
                case "devtools": Target().OpenDevTools(); break;
                case "theme": SetGlobalTheme(Arg(1)); break;
                case "theme-of": SetInstanceTheme(Target(), Arg(2) == "global" ? null : Arg(2)); break;
                case "lang": SetLanguage(Arg(1)); break;
                case "setting": SetSetting(Target(), Arg(2), ParseValue(string.Join(' ', a.Skip(3)))); break;
                case "devmode": SetDeveloperMode(Arg(1) == "on"); break;
                case "guide": OpenDeveloperGuide(); break;
                case "new-widget": CreateWidget(string.Join(' ', a.Skip(1))); break;
                case "export": Packages.Export(Widget(), string.Join(' ', a.Skip(2)).Trim('"')); break;
                case "uninstall": Uninstall(Widget()); break;
                default: Log.Warn($"Unknown command '{a[0]}'"); break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or BridgeException or InvalidDataException or IOException)
        {
            Log.Warn($"Command failed: {ex.Message}");
        }
    }

    /// JSON when it parses (true, 42, "text", [..]); otherwise the raw text.
    static JsonElement ParseValue(string raw)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<JsonElement>(raw); }
        catch (JsonException) { return Json.Element(raw); }
    }

    // ───────────────────────── shutdown ─────────────────────────

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
        Log.Info("Exiting");
        marginWatch.Stop();
        fullscreenWatch.Stop();
        StopHotReload();
        foreach (var p in pendingPackages.Values) Packages.Cancel(p);
        menu?.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Config.SaveNow();
        Storage.Flush();
        foreach (var w in windows.Values.ToList()) w.Close();
        windows.Clear();
        Notifier.Dispose();
        Audio.Dispose();
        Media.Dispose();
        Keys?.Dispose();
        tray.Dispose();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Monitor.Dispose();
            Storage.Dispose();
            Config.Dispose();
        }
        base.Dispose(disposing);
    }
}

using System.Text.Json;

namespace FullWidget.Core;

sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public bool FirstRunDone { get; set; }
    /// "auto" follows Windows.
    public string Language { get; set; } = "auto";
    /// Turns off GPU acceleration and asks WebView2 to use less memory. Needs a restart.
    public bool LowResourceMode { get; set; }
    /// Enables DevTools (F12), reload (F5) and reload-on-save in widgets.
    public bool DeveloperMode { get; set; }
    public bool HideAll { get; set; }
    /// While a full-screen app (game, video, slideshow) covers a monitor, its widgets hide and stop.
    public bool PauseOnFullscreen { get; set; } = true;
    /// The global theme (sdk/themes/&lt;id&gt;).
    public string ThemeId { get; set; } = Themes.DefaultId;
    /// The user's global appearance tweaks on top of the theme; cleared when the theme changes.
    public Dictionary<string, JsonElement> Theme { get; set; } = new();
    /// Permissions the user allowed, per third-party widget id. Built-in widgets need none.
    public Dictionary<string, List<string>> Grants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// Widgets this user made with "新建組件": the user wrote them, so what they declare is allowed.
    public List<string> OwnWidgets { get; set; } = new();
    /// Lets Windows open .fwz files with FullWidget (double-click to install).
    public bool FileAssociation { get; set; } = true;
    /// The widget market's index.json; empty = the default (Core/Market.cs).
    public string? MarketUrl { get; set; }
    /// Look for a new FullWidget now and then (Core/Updater.cs). Off unless the user turns it on:
    /// nothing goes online unasked.
    public bool AutoUpdateCheck { get; set; }
    /// The newest version the user was told about, so each one is announced once.
    public string? UpdateNotified { get; set; }
    /// System-wide shortcuts by action (Shell/Hotkeys.cs), e.g. "notes": "Ctrl+Alt+N"; "" = off.
    public Dictionary<string, string> Hotkeys { get; set; } = FullWidget.Shell.Hotkeys.Defaults();
    public List<InstanceConfig> Instances { get; set; } = new();
}

/// One widget placed on the desktop. Several instances may share a widget type.
sealed class InstanceConfig
{
    public string Id { get; set; } = NewId();
    public string Widget { get; set; } = "";
    /// Top-left of the widget card in physical screen pixels (excludes the shadow margin).
    public int X { get; set; }
    public int Y { get; set; }
    /// Card size in device-independent pixels (96 DPI units).
    public double Width { get; set; }
    public double Height { get; set; }
    /// "desktop" | "normal" | "top"
    public string Layer { get; set; } = "desktop";
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    /// Overrides the global theme for this instance only; null follows the global theme.
    public string? ThemeId { get; set; }
    public Dictionary<string, JsonElement> Settings { get; set; } = new();
    public Dictionary<string, JsonElement> Appearance { get; set; } = new();
    public string? CustomCss { get; set; }

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];
}

/// Loads and saves config.json. Saves are batched so dragging a widget doesn't hammer the disk.
sealed class ConfigStore : IDisposable
{
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 600 };
    public AppConfig Data { get; private set; } = new();

    /// Raised on every change (each change schedules a save), e.g. so the control center can refresh.
    public event Action? Changed;

    public ConfigStore()
    {
        saveTimer.Tick += (_, _) => SaveNow();
    }

    public void Load()
    {
        var file = Paths.ConfigFile;
        if (!File.Exists(file)) { Data = new AppConfig(); return; }
        try
        {
            Data = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(file), Json.File) ?? new AppConfig();
            MigrateIds();
        }
        catch (Exception ex)
        {
            // Keep the broken file for the user, start fresh.
            var backup = file + $".broken-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(file, backup, overwrite: true);
            Log.Error($"Config unreadable, backed up to {backup}", ex);
            Data = new AppConfig();
        }
    }

    /// Built-in widgets were "deskwidgets.*" before the app was renamed; they are "fullwidget.*" now.
    void MigrateIds()
    {
        bool changed = false;
        foreach (var i in Data.Instances)
            if (Catalog.Migrate(i.Widget) is var id && id != i.Widget) { i.Widget = id; changed = true; }
        if (Data.Grants.Keys.Any(k => Catalog.Migrate(k) != k))
        {
            var grants = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in Data.Grants) grants[Catalog.Migrate(k)] = v;
            Data.Grants = grants;
            changed = true;
        }
        if (changed) { Log.Info("Config: built-in widget ids renamed to fullwidget.*"); SaveNow(); }
    }

    public void SaveSoon()
    {
        saveTimer.Stop();
        saveTimer.Start();
        Changed?.Invoke();
    }

    /// Tells listeners (the control center) that something changed, without saving.
    public void Notify() => Changed?.Invoke();

    public void SaveNow()
    {
        saveTimer.Stop();
        Changed?.Invoke();
        try { Json.WriteAtomic(Paths.ConfigFile, JsonSerializer.Serialize(Data, Json.File)); }
        catch (Exception ex) { Log.Error("Config save failed", ex); }
    }

    public void Dispose() => saveTimer.Dispose();
}

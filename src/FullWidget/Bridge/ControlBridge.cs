using System.Diagnostics;
using System.Text.Json;
using FullWidget.Core;
using FullWidget.Shell;
using FullWidget.Windows;

namespace FullWidget.Bridge;

/// The control center's API: one state snapshot, plus a call for each change it can make.
/// Every change goes through App, so menus, commands and the control center behave alike.
sealed class ControlBridge(App app, ControlCenter? window = null)
{
    public object State()
    {
        var cfg = app.Config.Data;
        return new
        {
            version = App.Version,
            lang = Strings.Lang,
            language = cfg.Language,
            autostart = AutoStart.Enabled,
            lowResource = cfg.LowResourceMode,
            lowResourceActive = app.LowResourceActive,
            developer = cfg.DeveloperMode,
            fileAssociation = app.FileAssociationActive,
            devRoot = Paths.DevRoot,
            hideAll = cfg.HideAll,
            pauseFullscreen = cfg.PauseOnFullscreen,
            hotkeys = app.Keys?.Status(),
            update = app.Updater.State(),
            portable = Paths.Portable,
            editMode = app.EditMode,
            dataDir = Paths.DataDir,
            widgetsDir = Paths.UserWidgetsDir,
            themeId = cfg.ThemeId,
            themeOverrides = cfg.Theme,
            globalLook = Appearance.Effective(cfg, null),
            themes = Themes.All.Select(t => new { id = t.Id, name = t.DisplayName, description = Text.Pick(t.Description) }),
            catalog = Each(Sorted(app.Catalog.All), m => m.Id, m => new
            {
                id = m.Id,
                name = m.DisplayName,
                description = Text.Pick(m.Description),
                version = m.Version,
                author = m.Author,
                builtin = m.Builtin,
                category = Categories.Of(m),
                search = string.Join(" ", Text.All(m.Name).Concat(Text.All(m.Description)).Append(m.Id)).ToLowerInvariant(),
                icon = Icon(m),
                preview = Previews.Url(m, cfg.ThemeId, Strings.Lang),
                permissions = m.Permissions.Where(Permissions.IsKnown).Distinct(StringComparer.OrdinalIgnoreCase),
                pending = Permissions.Pending(cfg, m),
                own = cfg.OwnWidgets.Contains(m.Id, StringComparer.OrdinalIgnoreCase),
                folder = m.Builtin ? null : Path.GetFileName(m.Dir),
                count = cfg.Instances.Count(i => i.Widget == m.Id),
            }),
            problems = app.Catalog.Problems.Select(x => new { folder = x.Folder, message = x.Message }),
            instances = Each(cfg.Instances, i => i.Id, i =>
            {
                var m = app.Catalog.Find(i.Widget);
                var w = app.WindowOf(i.Id);
                return new
                {
                    id = i.Id,
                    widget = i.Widget,
                    name = m?.DisplayName ?? i.Widget,
                    missing = m == null,
                    builtin = m?.Builtin ?? false,
                    pending = m == null ? new List<string>() : Permissions.Pending(cfg, m),
                    icon = m == null ? null : Icon(m),
                    x = i.X,
                    y = i.Y,
                    width = i.Width,
                    height = i.Height,
                    size = m?.Size,
                    layer = i.Layer,
                    locked = i.Locked,
                    clickThrough = i.ClickThrough,
                    themeId = i.ThemeId,
                    settings = w?.EffectiveSettings() ?? i.Settings,
                    schema = m == null ? null : Schema(m),
                    look = Appearance.Effective(cfg, i),
                    overrides = i.Appearance,
                    customCss = i.CustomCss,
                };
            }),
        };
    }

    /// Maps each item and checks right away that it can be sent, so one broken widget (an odd
    /// manifest, a setting that can't be written as JSON) is left out and logged, rather than
    /// the whole state failing and the control center staying blank.
    static List<object> Each<T>(IEnumerable<T> items, Func<T, string> name, Func<T, object> map)
    {
        var list = new List<object>();
        foreach (var x in items)
        {
            try
            {
                var o = map(x);
                JsonSerializer.SerializeToUtf8Bytes(o, Json.Web);
                list.Add(o);
            }
            catch (Exception ex) { Log.Error($"Control center: left out {name(x)}", ex); }
        }
        return list;
    }

    /// Built-ins in the order people look for them; everything else after, by id.
    /// Built-in widgets in library order, category by category.
    static readonly string[] BuiltinOrder =
    {
        "fullwidget.clock", "fullwidget.analogclock", "fullwidget.flipclock", "fullwidget.worldclock",
        "fullwidget.calendar", "fullwidget.countdown", "fullwidget.pomodoro", "fullwidget.timer", "fullwidget.progress",
        "fullwidget.weather", "fullwidget.forecast", "fullwidget.airquality", "fullwidget.sunmoon",
        "fullwidget.sysinfo", "fullwidget.usage", "fullwidget.netspeed", "fullwidget.disks", "fullwidget.battery",
        "fullwidget.network", "fullwidget.pcinfo", "fullwidget.recyclebin", "fullwidget.volume", "fullwidget.power",
        "fullwidget.notes", "fullwidget.todo", "fullwidget.launcher", "fullwidget.habits", "fullwidget.calculator", "fullwidget.search",
        "fullwidget.infocard", "fullwidget.rss", "fullwidget.currency", "fullwidget.dailyimage", "fullwidget.onthisday",
        "fullwidget.media", "fullwidget.photoframe", "fullwidget.whitenoise", "fullwidget.label",
    };

    public static int Order(string id) => Array.IndexOf(BuiltinOrder, id) is var i and >= 0 ? i : int.MaxValue;

    /// Library order: by category, built-in widgets first, then by name.
    public static IEnumerable<WidgetManifest> Sorted(IEnumerable<WidgetManifest> all) =>
        all.OrderBy(m => Categories.Order(Categories.Of(m))).ThenBy(m => !m.Builtin).ThenBy(m => Order(m.Id))
           .ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase);

    static object Schema(WidgetManifest m) => m.Settings.Select(s => new
    {
        key = s.Key,
        type = s.Type,
        label = Text.Pick(s.Label, s.Key),
        description = s.Description == null ? null : Text.Pick(s.Description),
        placeholder = s.Placeholder == null ? null : Text.Pick(s.Placeholder),
        min = s.Min,
        max = s.Max,
        step = s.Step,
        options = s.Options?.Select(o => new { value = o.Value, label = Text.Pick(o.Label, o.Value.ToString()) }),
    });

    static string? Icon(WidgetManifest m) => Icons.For(m);

    public Task<object?> HandleAsync(string method, JsonElement p)
    {
        var cfg = app.Config.Data;
        InstanceConfig Inst() => cfg.Instances.FirstOrDefault(i => i.Id == Str(p, "id")) ?? throw new BridgeException("no such widget");
        WidgetWindow Win() => app.WindowOf(Inst().Id) ?? throw new BridgeException("this widget is not running");
        bool On() => p.TryGetProperty("on", out var v) && v.ValueKind == JsonValueKind.True;
        WidgetManifest Widget() => app.Catalog.Find(Req(p, "widget")) ?? throw new BridgeException(Strings.NotInstalled);

        switch (method)
        {
            case "state.get": return Done(State());
            case "fonts.list": return Done(App.FontNames);

            case "instance.setting": app.SetSetting(Win(), Req(p, "key"), Prop(p, "value")); break;
            case "instance.pickFolder":
            {
                var w = Win();
                var spec = w.Manifest.Settings.FirstOrDefault(s => s.Key == Req(p, "key") && s.Type == "folder") ?? throw new BridgeException("not a folder setting");
                var now = w.EffectiveSettings().GetValueOrDefault(spec.Key);
                var chosen = Launcher.Pick("folder", null, Text.Pick(spec.Label) is { Length: > 0 } l ? l : null,
                    now.ValueKind == JsonValueKind.String ? now.GetString() : null);
                if (chosen != null) app.SetSetting(w, spec.Key, Json.Element(chosen));
                return Done(chosen);
            }
            case "instance.appearance":
            {
                var v = Prop(p, "value");
                app.SetAppearance(Win(), Req(p, "key"), v.ValueKind == JsonValueKind.Null ? null : v);
                break;
            }
            case "instance.css": app.SetCustomCss(Win(), Str(p, "css")); break;
            case "instance.theme": app.SetInstanceTheme(Win(), Str(p, "theme")); break;
            case "instance.layer": app.SetLayer(Win(), Req(p, "layer")); break;
            case "instance.locked": app.SetLocked(Win(), On()); break;
            case "instance.clickThrough": app.SetClickThrough(Win(), On()); break;
            case "instance.size": Win().SetSizeDip(Num(p, "width") ?? Inst().Width, Num(p, "height") ?? Inst().Height); break;
            case "instance.reload": Win().Reload(); break;
            case "instance.identify": Win().Identify(); break;
            case "instance.devtools": Win().OpenDevTools(); break;
            case "instance.remove": app.Remove(Inst()); break;
            case "instance.duplicate": return Done(new { id = app.Duplicate(Inst()).Id });

            case "widget.add":
            {
                var m = Widget();
                // A widget with permissions waiting for the user's OK is only added once the
                // page has shown them and the user agreed (grant: true).
                if (Permissions.Pending(cfg, m).Count > 0)
                {
                    if (!(p.TryGetProperty("grant", out var g) && g.ValueKind == JsonValueKind.True))
                        throw new BridgeException(Strings.NeedsPermission);
                    app.Grant(m);
                }
                return Done(new { id = app.AddWidget(m).Id });
            }
            case "widget.grant": app.Grant(Widget()); break;
            case "widget.uninstall": app.Uninstall(Widget()); break;
            case "widget.openFolder":
            {
                var m = Widget();
                Process.Start("explorer.exe", $"\"{m.Dir}\"");
                break;
            }
            case "widget.export":
            {
                var m = Widget();
                if (m.Builtin) throw new BridgeException(Strings.BuiltinCantExport);
                var file = window?.PickExportFile(Packages.FileName(m));
                if (file == null) return Done(null);
                try { Packages.Export(m, file); }
                catch (IOException ex) { throw new BridgeException(string.Format(Strings.ExportFailed, ex.Message)); }
                return Done(new { path = file, name = Path.GetFileName(file) });
            }
            case "widget.create":
            {
                try
                {
                    var (inst, dir) = app.CreateWidget(Req(p, "name"));
                    return Done(new { id = inst.Id, dir });
                }
                catch (InvalidDataException ex) { throw new BridgeException(ex.Message); }
                catch (IOException ex) { throw new BridgeException(string.Format(Strings.CreateFailed, ex.Message)); }
            }

            case "package.import":
            {
                var file = window?.PickPackage();
                if (file != null) app.Post(() => app.OpenPackage(file));
                break;
            }
            case "package.install": return Done(app.InstallPackage(Req(p, "token")));
            case "package.cancel": app.CancelPackage(Req(p, "token")); break;

            case "theme.global": app.SetGlobalTheme(Req(p, "id")); break;
            case "theme.override":
            {
                var v = Prop(p, "value");
                app.SetThemeOverride(Req(p, "key"), v.ValueKind == JsonValueKind.Null ? null : v);
                break;
            }
            case "theme.reset": app.ResetThemeOverrides(); break;

            case "app.language": app.SetLanguage(Req(p, "lang")); break;
            case "app.autostart": AutoStart.Enabled = On(); app.Config.SaveSoon(); break;
            case "app.lowResource": app.SetLowResource(On()); break;
            case "app.restart": app.Post(app.Restart); break;
            case "app.developer": app.SetDeveloperMode(On()); break;
            case "app.hideAll": app.SetHideAll(On()); break;
            case "app.pauseFullscreen": app.SetPauseOnFullscreen(On()); break;
            case "app.hotkey": app.SetShortcut(Req(p, "action"), Str(p, "keys") ?? ""); break;
            case "app.updateCheck": return app.Updater.CheckAsync(manual: true).ContinueWith(_ => (object?)null);
            case "app.updateInstall": _ = app.Updater.InstallAsync(); break;
            case "app.updateCancel": app.Updater.Cancel(); break;
            case "app.updateAuto": app.Updater.SetAuto(On()); break;
            case "app.updatePage": app.Updater.OpenPage(); break;
            case "app.hotkeysPause": app.Keys?.Pause(On()); break;
            case "app.lockAll": app.SetAllLocked(On()); break;
            case "app.editMode": app.SetEditMode(On()); break;
            case "app.rescan": app.RescanWidgets(); break;
            case "app.fileAssociation": app.SetFileAssociation(On()); break;
            case "app.guide": app.OpenDeveloperGuide(); break;

            case "market.list": return app.Market.ListAsync(On() || (p.TryGetProperty("refresh", out var rf) && rf.ValueKind == JsonValueKind.True))!;
            case "market.install": return app.Market.InstallAsync(Req(p, "id"));
            case "market.open":
            {
                // the market's website, or a widget's page on it
                var url = Str(p, "url") is { Length: > 0 } u && u.StartsWith(app.Market.SiteUri.ToString(), StringComparison.OrdinalIgnoreCase) ? u : app.Market.SiteUri.ToString();
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                break;
            }
            case "app.reveal":
                Process.Start("explorer.exe", $"/select,\"{Req(p, "path")}\"");
                break;
            case "app.openFolder":
                Process.Start("explorer.exe", Str(p, "which") == "data" ? Paths.DataDir : Paths.UserWidgetsDir);
                break;

            default: throw new BridgeException($"unknown method '{method}'");
        }
        return Done(null);
    }

    static Task<object?> Done(object? r) => Task.FromResult(r);

    static string? Str(JsonElement p, string key) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double? Num(JsonElement p, string key) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static string Req(JsonElement p, string key) =>
        Str(p, key) is { Length: > 0 } s ? s : throw new BridgeException($"missing parameter '{key}'");

    static JsonElement Prop(JsonElement p, string key) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) ? v : Json.Element<object?>(null);
}

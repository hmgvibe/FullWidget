using FullWidget.Bridge;
using FullWidget.Core;
using FullWidget.Windows;
using static FullWidget.Shell.PopItem;

namespace FullWidget.Shell;

/// What the tray menu and the widget right-click menu contain. Frequent switches sit right in
/// the menu; longer lists open as a page that slides in.
static class Menus
{
    static (string value, string label)[] Layers =>
        new[] { ("top", Strings.LayerTopShort), ("normal", Strings.LayerNormalShort), ("desktop", Strings.LayerDesktopShort) };
    static string LayerName(string layer) =>
        layer switch { "top" => Strings.LayerTopShort, "normal" => Strings.LayerNormalShort, _ => Strings.LayerDesktop };

    public static List<PopItem> ForInstance(App app, WidgetWindow w)
    {
        var cfg = app.Config.Data;
        var look = Appearance.Effective(cfg, w.Cfg);
        bool frosted = Appearance.Style(look) == "frosted";
        bool shadow = Appearance.Shadow(look);
        string layer = w.Cfg.Layer is "top" or "normal" ? w.Cfg.Layer : "desktop";
        string theme = w.Cfg.ThemeId == null ? Strings.FollowGlobalShort : Themes.Get(w.Cfg.ThemeId).DisplayName;

        var themes = new List<PopItem>
        {
            Check(Strings.Lang == "en" ? $"{Strings.FollowGlobalShort} ({Themes.Get(cfg.ThemeId).DisplayName})" : $"{Strings.FollowGlobalShort}（{Themes.Get(cfg.ThemeId).DisplayName}）", w.Cfg.ThemeId == null, () => app.SetInstanceTheme(w, null)),
            Sep,
        };
        themes.AddRange(Themes.All.Select(t => Check(t.DisplayName, w.Cfg.ThemeId == t.Id, () => app.SetInstanceTheme(w, t.Id))));

        var items = new List<PopItem>
        {
            Header(w.Manifest.DisplayName, $"{LayerName(layer)} · {theme}", Icons.For(w.Manifest)),
            Item(Strings.WidgetSettings, () => app.OpenControlCenter(w.Cfg.Id)),
            Sep,
            Toggle(Strings.Lock, w.Cfg.Locked, () => app.SetLocked(w, !w.Cfg.Locked)),
            Toggle(Strings.ClickThrough, w.Cfg.ClickThrough, () => app.SetClickThrough(w, !w.Cfg.ClickThrough)),
            Seg(Strings.Layer, layer, Layers, v => app.SetLayer(w, v)),
            Seg(Strings.Card, frosted ? "frosted" : "transparent",
                new[] { ("transparent", Strings.StyleTransparent), ("frosted", Strings.StyleFrosted) },
                v => app.SetAppearance(w, "style", v)),
            Toggle(Strings.Shadow, shadow, () => app.SetAppearance(w, "shadow", !shadow), disabled: frosted),
            Sub(Strings.Theme, theme, themes),
            Sep,
            Item(Strings.Locate, w.Identify),
            Item(Strings.Reload, w.Reload),
        };
        if (cfg.DeveloperMode) items.Add(Item(Strings.DevTools, w.OpenDevTools));
        items.Add(Sep);
        items.Add(Item(Strings.Remove, () => app.Remove(w.Cfg), danger: true, confirm: Strings.RemoveConfirmShort));
        return items;
    }

    public static List<PopItem> ForTray(App app)
    {
        var cfg = app.Config.Data;
        var windows = app.Windows.ToList();

        // one page per category, so no page grows taller than the screen
        var add = ControlBridge.Sorted(app.Catalog.All)
            .GroupBy(Categories.Of)
            .Select(g => Sub(Categories.Name(g.Key), g.Count().ToString(), g.Select(m =>
            {
                int count = cfg.Instances.Count(i => i.Widget == m.Id);
                return Item(m.DisplayName, () => app.RequestAdd(m), hint: count > 0 ? Strings.AlreadyHave(count) : null, icon: Icons.For(m));
            }).ToList()))
            .ToList();
        add.Add(Sep);
        add.Add(Item(Strings.NewWidgetEllipsis, () => app.OpenControlCenter(page: "create")));

        var counts = windows.GroupBy(w => w.Manifest.Id).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        var mine = windows.Select(w =>
        {
            string name = w.Manifest.DisplayName;
            if (counts[w.Manifest.Id] > 1) name += $" #{seen[w.Manifest.Id] = seen.GetValueOrDefault(w.Manifest.Id) + 1}";
            return Item(name, () => app.OpenControlCenter(w.Cfg.Id), hint: LayerName(w.Cfg.Layer), icon: Icons.For(w.Manifest));
        }).ToList();

        var themes = Themes.All.Select(t => Check(t.DisplayName, cfg.ThemeId == t.Id, () => app.SetGlobalTheme(t.Id))).ToList();
        bool allLocked = windows.Count > 0 && windows.All(w => w.Cfg.Locked);

        var list = new List<PopItem>
        {
            Header(Strings.AppName, Strings.TrayHeader(App.Version, windows.Count), Icons.App),
            Item(Strings.OpenControlCenter, () => app.OpenControlCenter()),
            Sep,
            Toggle(Strings.ArrangeMode, app.EditMode, () => app.SetEditMode(!app.EditMode)),
            Toggle(Strings.LockAll, allLocked, () => app.SetAllLocked(!allLocked), disabled: windows.Count == 0),
            Toggle(Strings.HideAll, cfg.HideAll, () => app.SetHideAll(!cfg.HideAll)),
            Sep,
            Sub(Strings.AddWidget, null, add),
            Sub(Strings.MyWidgets, windows.Count.ToString(), mine),
            Sub(Strings.Theme, Themes.Get(cfg.ThemeId).DisplayName, themes),
            Sep,
            Item(Strings.SettingsEllipsis, () => app.OpenControlCenter(page: "settings")),
            Item(Strings.Exit, app.ExitThread),
        };
        // a new version: offered just under the header
        if (app.Updater.Available is { } v) list.Insert(2, Item(Strings.UpdateTo(v), () => app.OpenControlCenter(page: "settings")));
        return list;
    }
}

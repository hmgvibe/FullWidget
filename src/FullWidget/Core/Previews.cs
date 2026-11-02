namespace FullWidget.Core;

/// Preview pictures for the widget library. widget.json may name one per theme and language
/// ("preview/{theme}.{lang}.webp"); the control center reads them straight from the widget folders.
static class Previews
{
    public const string BuiltinHost = "builtin-widgets.fullwidget.local";
    public const string UserHost = "user-widgets.fullwidget.local";

    static readonly string[] Langs = { "zh-TW", "zh-CN", "en" };

    /// The best picture for this theme and language: the exact one, else the same theme in another
    /// language, else another theme. Null when the widget has none.
    public static string? Url(WidgetManifest m, string theme, string lang)
    {
        if (string.IsNullOrWhiteSpace(m.Preview)) return null;
        var themes = new[] { theme }.Concat(Themes.All.Select(t => t.Id)).Distinct();
        foreach (var th in themes)
            foreach (var lg in new[] { lang }.Concat(Langs).Distinct())
            {
                var rel = m.Preview.Replace("{theme}", th).Replace("{lang}", lg).Replace(Path.DirectorySeparatorChar, '/');
                var file = Path.GetFullPath(Path.Combine(m.Dir, rel));
                if (!file.StartsWith(m.Dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) continue;
                string host = m.Builtin ? BuiltinHost : UserHost;
                string folder = Uri.EscapeDataString(Path.GetFileName(m.Dir));
                string path = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
                return $"https://{host}/{folder}/{path}?v={File.GetLastWriteTimeUtc(file).Ticks:x}";
            }
        return null;
    }
}

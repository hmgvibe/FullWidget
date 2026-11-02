using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FullWidget.Core;

/// The shared look every widget gets:
///   built-in defaults ← theme defaults (theme.json) ← user's global tweaks ← per-instance overrides.
/// Each layer is emitted as CSS variables, so widgets only need to use var(--w-*).
static class Appearance
{
    public static readonly Dictionary<string, JsonElement> Defaults = new()
    {
        ["style"] = Json.Element("transparent"),   // transparent | frosted
        ["background"] = Json.Element("#161822"),
        ["backgroundOpacity"] = Json.Element(0.62),
        ["textColor"] = Json.Element("#ffffff"),
        ["accentColor"] = Json.Element("#4cc9f0"),
        ["fontFamily"] = Json.Element(""),
        ["fontScale"] = Json.Element(1.0),
        ["radius"] = Json.Element(16),
        ["shadow"] = Json.Element(true),
        ["padding"] = Json.Element(16),
        ["opacity"] = Json.Element(1.0),
    };

    /// Room around the card for its CSS shadow (transparent style only).
    public const int ShadowMarginDip = 20;

    public static ThemeInfo ThemeOf(AppConfig cfg, InstanceConfig? inst) => Themes.Get(inst?.ThemeId ?? cfg.ThemeId);

    /// Theme defaults plus the user's global tweaks: the keys the :root layer sets.
    static Dictionary<string, JsonElement> ThemeLayer(AppConfig cfg, InstanceConfig? inst)
    {
        var d = new Dictionary<string, JsonElement>();
        foreach (var (k, v) in ThemeOf(cfg, inst).Appearance) if (Defaults.ContainsKey(k)) d[k] = v;
        foreach (var (k, v) in cfg.Theme) if (Defaults.ContainsKey(k)) d[k] = v;
        return Resolve(d);
    }

    public static Dictionary<string, JsonElement> Effective(AppConfig cfg, InstanceConfig? inst)
    {
        var d = new Dictionary<string, JsonElement>(Defaults);
        foreach (var (k, v) in ThemeLayer(cfg, inst)) d[k] = v;
        if (inst != null)
            foreach (var (k, v) in inst.Appearance) if (Defaults.ContainsKey(k)) d[k] = v;
        return Resolve(d);
    }

    /// "system" as a colour means the Windows accent colour.
    static Dictionary<string, JsonElement> Resolve(Dictionary<string, JsonElement> d)
    {
        if (Str(d, "accentColor") == "system") d["accentColor"] = Json.Element(Themes.SystemAccent);
        return d;
    }

    /// The two style blocks injected into a widget page: theme (:root) and instance (html:root).
    public static (string themeCss, string userCss) PageCss(AppConfig cfg, InstanceConfig inst)
    {
        var themeLayer = ThemeLayer(cfg, inst);
        var themeMerged = new Dictionary<string, JsonElement>(Defaults);
        foreach (var (k, v) in themeLayer) themeMerged[k] = v;
        var all = Effective(cfg, inst);

        string theme = Css(themeLayer, themeMerged, ":root", null)
            .Insert(":root{".Length, $"--w-system-accent:{Themes.SystemAccent};");
        // The margin is structural (the host sizes the window around it), so it is always set here.
        string user = Css(Resolve(new(inst.Appearance)), all, "html:root", MarginDip(all));
        if (!string.IsNullOrWhiteSpace(inst.CustomCss)) user += "\n" + inst.CustomCss.Replace("</", "<\\/");
        return (theme, user);
    }

    public static string Style(Dictionary<string, JsonElement> a) => Str(a, "style") == "frosted" ? "frosted" : "transparent";
    public static bool Shadow(Dictionary<string, JsonElement> a) => a.TryGetValue("shadow", out var v) && v.ValueKind == JsonValueKind.True;
    public static int MarginDip(Dictionary<string, JsonElement> a) => Style(a) == "transparent" && Shadow(a) ? ShadowMarginDip : 0;

    /// Tint for the frosted (blur) backdrop, in the ABGR format the accent API wants.
    public static uint FrostedTint(Dictionary<string, JsonElement> a)
    {
        var (r, g, b) = ParseColor(Str(a, "background")) ?? (22, 24, 34);
        double op = Math.Clamp(Num(a, "backgroundOpacity", 0.62), 0, 1);
        uint alpha = (uint)Math.Round(0x10 + op * 0x60);
        return (alpha << 24) | ((uint)b << 16) | ((uint)g << 8) | (uint)r;
    }

    /// CSS for one layer. Theme uses :root; instance overrides use html:root so they beat the
    /// widget author's own :root rules, while the theme does not.
    /// <param name="layer">Only the keys this layer sets.</param>
    /// <param name="merged">Values in effect at this layer (defaults + layers below), used for
    /// variables that combine several keys, such as the background colour.</param>
    public static string Css(Dictionary<string, JsonElement> layer, Dictionary<string, JsonElement> merged,
                             string selector, int? marginDip)
    {
        var sb = new StringBuilder(selector).Append('{');
        void Var(string name, string value) => sb.Append("--w-").Append(name).Append(':').Append(value).Append(';');

        if (layer.ContainsKey("background") || layer.ContainsKey("backgroundOpacity") || layer.ContainsKey("style"))
        {
            var full = merged;
            bool frosted = Style(full) == "frosted";
            var (r, g, b) = ParseColor(Str(full, "background")) ?? (22, 24, 34);
            double op = Math.Clamp(Num(full, "backgroundOpacity", 0.62), 0, 1);
            if (frosted) op *= 0.45; // the blur already gives the card body
            Var("bg", $"rgba({r},{g},{b},{F(op)})");
            Var("bg-solid", $"rgb({r},{g},{b})"); // the card colour without transparency, for cut-outs and gaps
        }
        if (Has(layer, "textColor", out var fg)) Var("fg", Safe(fg));
        if (Has(layer, "accentColor", out var acc)) Var("accent", Safe(acc));
        if (Has(layer, "fontFamily", out var font) && font.Length > 0) Var("font", Safe(font) + ",var(--w-font-fallback)");
        if (layer.ContainsKey("fontScale")) Var("font-scale", F(Math.Clamp(Num(layer, "fontScale", 1), 0.5, 3)));
        if (layer.ContainsKey("radius")) Var("radius", F(Math.Clamp(Num(layer, "radius", 16), 0, 64)) + "px");
        if (layer.ContainsKey("padding")) Var("padding", F(Math.Clamp(Num(layer, "padding", 16), 0, 64)) + "px");
        if (layer.ContainsKey("opacity")) Var("opacity", F(Math.Clamp(Num(layer, "opacity", 1), 0.1, 1)));
        // Not "none": base.css combines this with an inset border in one box-shadow list.
        if (layer.ContainsKey("shadow") && !Shadow(layer)) Var("shadow", "0 0 0 transparent");
        if (marginDip is int m) Var("margin", m + "px");
        return sb.Append('}').ToString();
    }

    static bool Has(Dictionary<string, JsonElement> d, string key, out string value)
    {
        value = Str(d, key);
        return d.ContainsKey(key);
    }

    static string Str(Dictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static double Num(Dictionary<string, JsonElement> d, string key, double def) =>
        d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// Strips characters that could break out of a CSS declaration.
    static string Safe(string s) => new(s.Where(c => c is not (';' or '{' or '}' or '<' or '>' or '\\')).ToArray());

    public static (int r, int g, int b)? ParseColor(string s)
    {
        s = s.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => $"{c}{c}"));
        if (s.Length is not (6 or 8) || !int.TryParse(s[..6], NumberStyles.HexNumber, null, out var v)) return null;
        return ((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }
}

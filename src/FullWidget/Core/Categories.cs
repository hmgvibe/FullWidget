using FullWidget.Shell;

namespace FullWidget.Core;

/// Groups in the widget library ("category" in widget.json). Widgets without a known one are "other".
static class Categories
{
    public static readonly string[] Known = { "time", "weather", "system", "productivity", "info", "media" };

    public static string Of(WidgetManifest m)
    {
        var c = (m.Category ?? "").Trim().ToLowerInvariant();
        return Known.Contains(c) ? c : "other";
    }

    public static int Order(string category) => Array.IndexOf(Known, category) is var i and >= 0 ? i : Known.Length;

    public static string Name(string category) => category switch
    {
        "time" => Strings.CategoryTime,
        "weather" => Strings.CategoryWeather,
        "system" => Strings.CategorySystem,
        "productivity" => Strings.CategoryProductivity,
        "info" => Strings.CategoryInfo,
        "media" => Strings.CategoryMedia,
        _ => Strings.CategoryOther,
    };
}

using System.Globalization;
using System.Text.Json;

namespace FullWidget.Core;

/// Picks the right language from manifest strings like "時鐘" or { "zh-TW": "時鐘", "en": "Clock" }.
static class Text
{
    public static string Locale { get; set; } = CultureInfo.CurrentUICulture.Name;

    public static string Pick(JsonElement? value, string fallback = "")
    {
        if (value is not { } v) return fallback;
        if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? fallback;
        if (v.ValueKind != JsonValueKind.Object) return fallback;

        foreach (var key in Candidates(Locale))
            foreach (var p in v.EnumerateObject())
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString()!;
        foreach (var p in v.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString()!;
        return fallback;
    }

    /// A value given per language ({ "zh-TW": "google", "zh-CN": "baidu", "en": "google" }) → the
    /// one for the UI language; anything else is returned as it is.
    public static JsonElement PickValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return value;
        var props = value.EnumerateObject().ToList();
        if (props.Count == 0 || !props.All(p => LangKey.IsMatch(p.Name))) return value;
        foreach (var key in Candidates(Locale))
            foreach (var p in props)
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return props[0].Value;
    }

    static readonly System.Text.RegularExpressions.Regex LangKey = new(@"^[a-z]{2,3}(-[A-Za-z]{2,4})?$");

    /// Every language's text, for searching.
    public static IEnumerable<string> All(JsonElement? value)
    {
        if (value is not { } v) yield break;
        if (v.ValueKind == JsonValueKind.String) { yield return v.GetString() ?? ""; yield break; }
        if (v.ValueKind != JsonValueKind.Object) yield break;
        foreach (var p in v.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) yield return p.Value.GetString() ?? "";
    }

    static IEnumerable<string> Candidates(string locale)
    {
        yield return locale;
        var lower = locale.ToLowerInvariant();
        if (lower.StartsWith("zh"))
        {
            bool hans = lower is "zh-cn" or "zh-sg" or "zh-hans" || lower.StartsWith("zh-hans");
            if (hans) { yield return "zh-CN"; yield return "zh-Hans"; yield return "zh"; yield return "zh-TW"; }
            else { yield return "zh-TW"; yield return "zh-Hant"; yield return "zh-HK"; yield return "zh"; yield return "zh-CN"; }
        }
        else
        {
            var dash = locale.IndexOf('-');
            if (dash > 0) yield return locale[..dash];
        }
        yield return "en";
    }
}

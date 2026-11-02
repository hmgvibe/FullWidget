using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace FullWidget.Core;

/// A global look: sdk/themes/&lt;id&gt;/theme.json (name + appearance defaults the host needs to
/// know, e.g. card style and colours) plus theme.css (everything else, loaded by the page).
sealed class ThemeInfo
{
    public string Id { get; set; } = "";
    public JsonElement? Name { get; set; }
    public JsonElement? Description { get; set; }
    public Dictionary<string, JsonElement> Appearance { get; set; } = new();

    [JsonIgnore] public string DisplayName => Text.Pick(Name, Id);
    [JsonIgnore] public string CssHref => $"/__fw/themes/{Id}/theme.css";
}

static class Themes
{
    public const string DefaultId = "instrument";

    static readonly List<ThemeInfo> all = new();
    static readonly ThemeInfo Fallback = new() { Id = DefaultId };

    public static IReadOnlyList<ThemeInfo> All => all;

    /// The Windows accent colour, in the lighter shade Windows itself uses on dark surfaces.
    public static string SystemAccent { get; private set; } = "#60cdff";

    public static ThemeInfo Get(string? id) =>
        all.FirstOrDefault(t => t.Id == id) ?? all.FirstOrDefault(t => t.Id == DefaultId) ?? Fallback;

    public static void Load()
    {
        all.Clear();
        var dir = Paths.ThemesDir;
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "theme.json", SearchOption.AllDirectories))
            {
                try
                {
                    var t = JsonSerializer.Deserialize<ThemeInfo>(File.ReadAllText(file), Json.File);
                    if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                    all.Add(t);
                }
                catch (Exception ex) { Log.Warn($"Theme {file} skipped: {ex.Message}"); }
            }
        }
        // Keep the default first, the rest in a stable order.
        all.Sort((a, b) => a.Id == DefaultId ? -1 : b.Id == DefaultId ? 1 : string.CompareOrdinal(a.Id, b.Id));
        RefreshSystemAccent();
        Log.Info($"Themes: {string.Join(", ", all.Select(t => t.Id))}; system accent {SystemAccent}");
    }

    public static void RefreshSystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            // AccentPalette: 8 RGBA entries from lightest to darkest; entry 1 is "light 2".
            if (key?.GetValue("AccentPalette") is byte[] p && p.Length >= 8)
            {
                SystemAccent = $"#{p[4]:x2}{p[5]:x2}{p[6]:x2}";
                return;
            }
            using var dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (dwm?.GetValue("AccentColor") is int abgr)
                SystemAccent = $"#{abgr & 0xFF:x2}{(abgr >> 8) & 0xFF:x2}{(abgr >> 16) & 0xFF:x2}";
        }
        catch (Exception ex) { Log.Warn($"System accent unreadable: {ex.Message}"); }
    }
}

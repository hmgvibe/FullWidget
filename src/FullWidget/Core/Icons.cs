using FullWidget.Windows;

namespace FullWidget.Core;

/// Icons as data: URIs, for pages that can't read widget folders (control center, menus).
static class Icons
{
    static readonly Dictionary<string, (DateTime stamp, string uri)> cache = new();

    /// The app's logo (build/logo/logo.svg).
    public const string App =
        "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'>" +
        "<rect x='4' y='4' width='18' height='56' rx='7' fill='%234cc9f0'/><rect x='26' y='4' width='34' height='18' rx='7' fill='%239b5de5'/><rect x='26' y='26' width='20' height='16' rx='7' fill='%23ffd166'/><circle cx='51.5' cy='51.5' r='8.5' fill='%2306d6a0'/></svg>";

    public static string? For(WidgetManifest? m)
    {
        if (m == null || string.IsNullOrEmpty(m.Icon)) return null;
        var file = Path.GetFullPath(Path.Combine(m.Dir, m.Icon));
        if (!file.StartsWith(m.Dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) return null;
        var stamp = File.GetLastWriteTimeUtc(file);
        if (cache.TryGetValue(file, out var c) && c.stamp == stamp) return c.uri;
        var uri = $"data:{Mime.For(file).Split(';')[0]};base64,{Convert.ToBase64String(File.ReadAllBytes(file))}";
        cache[file] = (stamp, uri);
        return uri;
    }
}

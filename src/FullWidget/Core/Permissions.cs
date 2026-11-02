namespace FullWidget.Core;

/// What a widget may do. A widget declares permissions in widget.json; a third-party widget gets
/// them only after the user allows them (when installing or adding it). Built-in widgets and
/// widgets the user made here are trusted with what they declare.
static class Permissions
{
    /// "system", "notify", "audio", "media", "shell.open", "shell.launch", "network:&lt;host&gt;", "network:*.&lt;domain&gt;" or "network:*".
    public static bool IsKnown(string p) =>
        p is "system" or "notify" or "audio" or "media" or "shell.open" or "shell.launch" || (p.StartsWith("network:", StringComparison.Ordinal) && p.Length > "network:".Length);

    public static bool Trusted(AppConfig cfg, WidgetManifest m) =>
        m.Builtin || cfg.OwnWidgets.Contains(m.Id, StringComparer.OrdinalIgnoreCase);

    /// The declared permissions the widget may use right now.
    public static List<string> Allowed(AppConfig cfg, WidgetManifest m)
    {
        var known = m.Permissions.Where(IsKnown).Distinct(StringComparer.OrdinalIgnoreCase);
        if (Trusted(cfg, m)) return known.ToList();
        var granted = cfg.Grants.GetValueOrDefault(m.Id) ?? new List<string>();
        return known.Where(p => granted.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// Declared permissions still waiting for the user's OK.
    public static List<string> Pending(AppConfig cfg, WidgetManifest m)
    {
        var allowed = Allowed(cfg, m);
        return m.Permissions.Where(IsKnown).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !allowed.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// Allows everything the widget declares (what the user just saw and accepted).
    public static void Grant(AppConfig cfg, WidgetManifest m) =>
        cfg.Grants[m.Id] = m.Permissions.Where(IsKnown).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool Has(IEnumerable<string> allowed, string permission) =>
        allowed.Contains(permission, StringComparer.OrdinalIgnoreCase);

    /// Network hosts from the allowed permissions: "api.example.com", "*.example.com" or "*".
    public static IEnumerable<string> Hosts(IEnumerable<string> allowed) =>
        allowed.Where(p => p.StartsWith("network:", StringComparison.OrdinalIgnoreCase))
               .Select(p => p["network:".Length..].Trim().ToLowerInvariant())
               .Where(h => h.Length > 0);

    public static bool AllowsHost(IEnumerable<string> allowed, string host)
    {
        host = host.ToLowerInvariant();
        foreach (var pattern in Hosts(allowed))
        {
            if (pattern == "*" || pattern == host) return true;
            if (pattern.StartsWith("*.") && (host == pattern[2..] || host.EndsWith(pattern[1..]))) return true;
        }
        return false;
    }

    /// Content-Security-Policy for a widget page: it may load from its own origin, and reach only
    /// the hosts it is allowed to. Inline scripts and eval stay allowed so any framework works.
    public static string ContentSecurityPolicy(IEnumerable<string> allowed)
    {
        var sources = new List<string>();
        foreach (var h in Hosts(allowed).Distinct())
        {
            if (h == "*") { sources.AddRange(new[] { "https:", "http:", "wss:", "ws:" }); continue; }
            var hosts = h.StartsWith("*.") ? new[] { h[2..], h } : new[] { h };
            foreach (var x in hosts)
                if (System.Text.RegularExpressions.Regex.IsMatch(x, @"^(\*\.)?[a-z0-9.-]+(:\d+)?$"))
                    sources.AddRange(new[] { $"https://{x}", $"http://{x}", $"wss://{x}", $"ws://{x}" });
        }
        string net = string.Join(' ', sources.Distinct());
        return $"default-src 'self' data: blob: {net}; " +
               $"script-src 'self' 'unsafe-inline' 'unsafe-eval' blob: {net}; " +
               $"style-src 'self' 'unsafe-inline' data: {net}; " +
               "form-action 'none'; base-uri 'self'; object-src 'none'";
    }
}

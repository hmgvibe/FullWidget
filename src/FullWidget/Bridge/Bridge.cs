using FullWidget.Shell;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FullWidget.Core;
using FullWidget.Windows;

namespace FullWidget.Bridge;

/// An error meant for the widget (shown to its JS as a rejected promise), not a host bug.
sealed class BridgeException(string message) : Exception(message);

/// Handles calls from widget pages. Every call is checked against the widget's manifest
/// permissions; built-in widgets go through the same checks.
sealed class WidgetBridge(App app)
{
    const int MaxFetchBytes = 8 * 1024 * 1024;

    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // redirects are followed by hand so each hop is permission-checked
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // A connection left idle can be dropped by a router or proxy without a word; a request
            // sent on it then just waits out the timeout. Reuse only ones idle for a few seconds.
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"FullWidget/{App.Version}");
        return c;
    }

    /// `files`: paths of files the page sent along (dropped on it), for "shell.paths".
    public async Task<object?> HandleAsync(WidgetWindow w, string method, JsonElement p, IReadOnlyList<string>? files = null)
    {
        string id = w.Cfg.Id;
        switch (method)
        {
            case "ready": w.OnPageReady(); return null;
            case "log":
                Log.Info($"[widget {w.Manifest.Id}/{id}] {Str(p, "level") ?? "info"}: {Str(p, "msg")}");
                return null;

            case "window.drag": w.BeginDrag(Num(p, "dx") ?? 0, Num(p, "dy") ?? 0); return null;
            case "window.resize": w.BeginResize(); return null;
            case "window.menu": app.Post(() => app.ShowInstanceMenu(w)); return null;
            case "window.devtools":
                if (!app.Config.Data.DeveloperMode) throw new BridgeException("DevTools are available in developer mode only");
                app.Post(w.OpenDevTools);
                return null;
            case "window.getSize": return w.SizeInfo();
            case "window.setSize":
                w.SetSizeDip(Num(p, "width") ?? w.Cfg.Width, Num(p, "height") ?? w.Cfg.Height);
                return w.SizeInfo();

            case "settings.get": return w.EffectiveSettings();
            case "settings.set": app.SetSetting(w, Req(p, "key"), Prop(p, "value")); return null;

            case "storage.get": return app.Storage.Get(id, Req(p, "key"));
            case "storage.set": app.Storage.Set(id, Req(p, "key"), Prop(p, "value")); return null;
            case "storage.remove": app.Storage.Remove(id, Req(p, "key")); return null;
            case "storage.keys": return app.Storage.Keys(id);

            case "system.subscribe":
                Require(w, "system");
                var topics = p.TryGetProperty("topics", out var t) && t.ValueKind == JsonValueKind.Array
                    ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                    : new List<string>();
                return app.Monitor.Subscribe(w, topics, (int)(Num(p, "interval") ?? 1000));
            case "system.unsubscribe": app.Monitor.Unsubscribe(w); return null;
            case "system.history": Require(w, "system"); return app.Monitor.History();
            case "system.info": Require(w, "system"); return Providers.SystemInfo.Info();
            case "system.adapters": Require(w, "system"); return Providers.SystemInfo.Adapters();
            case "system.ping":
                Require(w, "system");
                return await Providers.SystemInfo.PingAsync(id, Req(p, "host"), (int)(Num(p, "timeout") ?? 1500));

            case "audio.get": Require(w, "audio"); return app.Audio.State();
            case "audio.set":
                Require(w, "audio");
                return app.Audio.Set(Num(p, "volume"), p.TryGetProperty("muted", out var mu) && mu.ValueKind is JsonValueKind.True or JsonValueKind.False ? mu.GetBoolean() : null);
            case "audio.subscribe": Require(w, "audio"); return app.Audio.Subscribe(w);
            case "audio.unsubscribe": app.Audio.Unsubscribe(w); return null;

            case "media.get": Require(w, "media"); return await app.Media.GetAsync();
            case "media.subscribe": Require(w, "media"); return await app.Media.SubscribeAsync(w);
            case "media.unsubscribe": app.Media.Unsubscribe(w); return null;
            case "media.lyrics": Require(w, "media"); return await app.Media.LyricsAsync();
            case "media.control":
                Require(w, "media");
                return await app.Media.ControlAsync(Req(p, "action"), Num(p, "position"));

            // a folder the user chose in a "folder" setting; choosing it is the permission
            case "files.list":
            {
                string key = Req(p, "key");
                return await Providers.FolderFiles.ListAsync(Providers.FolderFiles.Root(w, app, key), key,
                    p.TryGetProperty("recursive", out var rec) && rec.ValueKind == JsonValueKind.True);
            }
            case "files.info":
            {
                string root = Providers.FolderFiles.Root(w, app, Req(p, "key"));
                string full = Providers.FolderFiles.Resolve(root, Req(p, "path")) ?? throw new BridgeException("no such picture in the folder");
                return await Task.Run(() => Providers.FolderFiles.Info(full));
            }
            case "files.pick":
            {
                string key = Req(p, "key");
                var spec = w.Manifest.Settings.FirstOrDefault(s => s.Key == key);
                if (spec?.Type != "folder") throw new BridgeException($"'{key}' is not a setting of type \"folder\" in widget.json");
                var now = w.EffectiveSettings().GetValueOrDefault(spec.Key);
                var chosen = Launcher.Pick("folder", null, Text.Pick(spec.Label) is { Length: > 0 } l ? l : null,
                    now.ValueKind == JsonValueKind.String ? now.GetString() : null);
                if (chosen != null) app.SetSetting(w, key, Json.Element(chosen));
                return chosen;
            }

            // built-in widgets only: these change the PC or delete files for good
            case "recycle.query": RequireBuiltin(w); return PowerActions.RecycleBin();
            case "recycle.empty": RequireBuiltin(w); return await PowerActions.EmptyRecycleBinAsync();
            case "recycle.open": RequireBuiltin(w); PowerActions.OpenRecycleBin(); return null;
            case "power.do": RequireBuiltin(w); PowerActions.Do(Req(p, "action")); return null;

            case "net.fetch": return await FetchAsync(w, p);

            case "notify.show":
                Require(w, "notify");
                app.Notifier.Schedule(id, Str(p, "tag"), Num(p, "at") is double at ? DateTimeOffset.FromUnixTimeMilliseconds((long)at).ToLocalTime() : null,
                    Str(p, "title") ?? w.Manifest.DisplayName, Str(p, "body") ?? "");
                return null;
            case "notify.cancel":
                Require(w, "notify");
                app.Notifier.Cancel(id, Str(p, "tag"));
                return null;

            case "shell.launch":
                Require(w, "shell.launch");
                Launcher.Launch(Req(p, "path"));
                return null;
            case "shell.info":
                Require(w, "shell.launch");
                return Launcher.Info(Req(p, "path"), (int)(Num(p, "size") ?? 64));
            case "shell.pick":
                Require(w, "shell.launch");
                return Launcher.Pick(Str(p, "type") == "folder" ? "folder" : "file", Str(p, "filter"), Str(p, "title"));
            case "shell.paths":
                Require(w, "shell.launch");
                return files ?? Array.Empty<string>();

            case "text.chinese":
                return Launcher.ConvertChinese(Str(p, "text") ?? "", Str(p, "to") != "simplified");

            case "shell.open":
                Require(w, "shell.open");
                OpenUrl(Req(p, "target"));
                return null;

            default: throw new BridgeException($"unknown method '{method}'");
        }
    }

    void Require(WidgetWindow w, string permission)
    {
        if (!w.Manifest.Declares(permission))
            throw new BridgeException($"permission denied: add \"{permission}\" to permissions in widget.json");
        if (!Permissions.Has(w.Allowed, permission))
            throw new BridgeException(NotAllowedYet(permission));
    }

    static void RequireBuiltin(WidgetWindow w)
    {
        if (!w.Manifest.Builtin) throw new BridgeException("only FullWidget' built-in widgets can do this");
    }

    static string NotAllowedYet(string permission) =>
        $"permission \"{permission}\" is declared but the user has not allowed it yet " +
        $"(control center → {Strings.MyWidgets} → this widget → {Strings.Allow})";

    static Uri CheckUrl(WidgetWindow w, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            throw new BridgeException($"invalid url '{url}' (http/https only)");
        if (Permissions.AllowsHost(w.Allowed, u.Host)) return u;
        if (Permissions.AllowsHost(w.Manifest.Permissions.Where(Permissions.IsKnown), u.Host))
            throw new BridgeException(NotAllowedYet($"network:{u.Host}"));
        throw new BridgeException($"permission denied: add \"network:{u.Host}\" to permissions in widget.json");
    }

    static async Task<object> FetchAsync(WidgetWindow w, JsonElement p)
    {
        Uri uri = CheckUrl(w, Req(p, "url"));
        string method = (Str(p, "method") ?? "GET").ToUpperInvariant();
        string? body = Str(p, "body");
        bool base64 = Str(p, "as") == "base64";
        var headers = p.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object ? h : default;

        for (int hop = 0; hop < 6; hop++)
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), uri);
            string? contentType = null;
            if (headers.ValueKind == JsonValueKind.Object)
                foreach (var kv in headers.EnumerateObject())
                {
                    var value = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.ToString();
                    if (kv.Name.Equals("content-type", StringComparison.OrdinalIgnoreCase)) contentType = value;
                    else req.Headers.TryAddWithoutValidation(kv.Name, value);
                }
            if (body != null && method is not ("GET" or "HEAD"))
            {
                req.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
                req.Content.Headers.TryAddWithoutValidation("Content-Type", contentType ?? "text/plain; charset=utf-8");
            }

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            int status = (int)resp.StatusCode;
            if (status is >= 300 and < 400 && resp.Headers.Location is { } loc)
            {
                uri = CheckUrl(w, new Uri(uri, loc).ToString());
                if (status == 303 || (status is 301 or 302 && method == "POST")) { method = "GET"; body = null; }
                continue;
            }

            var bytes = await ReadLimitedAsync(resp.Content);
            var outHeaders = resp.Headers.Concat(resp.Content.Headers)
                .GroupBy(x => x.Key.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(x => x.Value)));
            string text = base64 ? Convert.ToBase64String(bytes) : Decode(bytes, resp.Content.Headers.ContentType?.CharSet);
            return new
            {
                url = uri.ToString(),
                status,
                statusText = resp.ReasonPhrase ?? "",
                ok = resp.IsSuccessStatusCode,
                headers = outHeaders,
                body = text,
                encoding = base64 ? "base64" : "text",
            };
        }
        throw new BridgeException("too many redirects");
    }

    static async Task<byte[]> ReadLimitedAsync(HttpContent content)
    {
        await using var s = await content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf)) > 0)
        {
            if (ms.Length + n > MaxFetchBytes) throw new BridgeException("response too large (8 MB limit)");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    static string Decode(byte[] bytes, string? charset)
    {
        try { if (!string.IsNullOrEmpty(charset)) return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes); }
        catch (ArgumentException) { /* unknown charset: fall back to UTF-8 */ }
        return Encoding.UTF8.GetString(bytes);
    }

    static void OpenUrl(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "mailto"))
            throw new BridgeException("shell.open only accepts http, https and mailto links");
        Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }

    // ── param helpers ──
    static string? Str(JsonElement p, string key) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double? Num(JsonElement p, string key) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static string Req(JsonElement p, string key) =>
        Str(p, key) is { Length: > 0 } s ? s : throw new BridgeException($"missing parameter '{key}'");

    static JsonElement Prop(JsonElement p, string key) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) ? v : Json.Element<object?>(null);
}

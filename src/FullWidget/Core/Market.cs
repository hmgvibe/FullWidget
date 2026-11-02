using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FullWidget.Bridge;
using FullWidget.Shell;

namespace FullWidget.Core;

/// The online widget market: a list of widgets (index.json, published from a GitHub repository to
/// GitHub Pages) and their .fwz packages. Installing downloads the package, checks its SHA-256
/// against the list, and hands it to the usual install sheet, where the user sees what it may do.
sealed class Market(App app)
{
    public const string DefaultIndex = "https://hmgvibe.github.io/fullwidget-market/index.json";
    const long MaxIndexBytes = 4 << 20, MaxPackageBytes = 16 << 20;
    static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var c = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"FullWidget/{App.Version}");
        return c;
    }

    JsonElement? index;      // the last list we got
    DateTime fetched;
    Uri? indexUri;

    /// Where the list is: FULLWIDGET_MARKET (for testing), else the setting, else the default.
    public Uri IndexUri
    {
        get
        {
            var s = Environment.GetEnvironmentVariable("FULLWIDGET_MARKET") is { Length: > 0 } env ? env
                : app.Config.Data.MarketUrl is { Length: > 0 } cfg ? cfg : DefaultIndex;
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || !Allowed(u)) u = new Uri(DefaultIndex);
            return u;
        }
    }

    /// The market's website (the folder of index.json).
    public Uri SiteUri => new(IndexUri, "./");

    /// https only; plain http just for a market on this PC (testing).
    static bool Allowed(Uri u) => u.Scheme == "https" || (u.Scheme == "http" && u.IsLoopback);

    static string Cache => Path.Combine(Paths.LocalDir, "market.json");

    /// { widgets: […], updated, site, offline }. Texts are in the app's language, addresses absolute.
    public async Task<object?> ListAsync(bool refresh)
    {
        var uri = IndexUri;
        bool offline = false;
        if (refresh || index == null || indexUri != uri || DateTime.UtcNow - fetched > Fresh)
        {
            try
            {
                var bytes = await GetAsync(uri, MaxIndexBytes);
                using var doc = JsonDocument.Parse(bytes);
                if (!doc.RootElement.TryGetProperty("widgets", out var w) || w.ValueKind != JsonValueKind.Array)
                    throw new BridgeException("not a widget market list");
                index = doc.RootElement.Clone();
                indexUri = uri;
                fetched = DateTime.UtcNow;
                try { File.WriteAllBytes(Cache, bytes); } catch (IOException) { }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or BridgeException or IOException)
            {
                Log.Warn($"Market list from {uri}: {ex.Message}");
                // offline: what we had, or what we saved last time
                if (index == null && File.Exists(Cache))
                    try { index = JsonDocument.Parse(File.ReadAllBytes(Cache)).RootElement.Clone(); indexUri = uri; }
                    catch (JsonException) { }
                if (index == null) throw new BridgeException(Strings.MarketUnreachable);
                offline = true;
            }
        }
        var root = index!.Value;
        return new
        {
            site = SiteUri.ToString(),
            updated = Str(root, "updated"),
            offline,
            widgets = root.GetProperty("widgets").EnumerateArray().Where(w => Str(w, "id") is { Length: > 0 }).Select(w => new
            {
                id = Str(w, "id"),
                name = w.TryGetProperty("name", out var n) ? Text.Pick(n, Str(w, "id") ?? "") : Str(w, "id"),
                description = w.TryGetProperty("description", out var d) ? Text.Pick(d) : "",
                author = Str(w, "author") ?? "",
                version = Str(w, "version") ?? "0.0.0",
                category = Str(w, "category") ?? "other",
                permissions = w.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Array
                    ? p.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : Array.Empty<string>(),
                icon = Abs(Str(w, "icon")),
                preview = Abs(Str(w, "preview")),
                previews = w.TryGetProperty("previews", out var pv) && pv.ValueKind == JsonValueKind.Array
                    ? pv.EnumerateArray().Select(x => Abs(x.GetString())).Where(x => x != null).ToArray() : Array.Empty<string?>(),
                bytes = w.TryGetProperty("bytes", out var b) && b.TryGetInt64(out var bl) ? bl : 0,
                updated = Str(w, "updated"),
                page = new Uri(SiteUri, "#" + Uri.EscapeDataString(Str(w, "id") ?? "")).ToString(),
            }).ToList(),
        };
    }

    /// Downloads a widget's package, checks it, and opens the install sheet in the control center.
    public async Task<object?> InstallAsync(string id)
    {
        if (index == null) await ListAsync(false);
        var entry = index!.Value.GetProperty("widgets").EnumerateArray().FirstOrDefault(w => Str(w, "id") == id);
        if (entry.ValueKind != JsonValueKind.Object) throw new BridgeException(Strings.MarketNoSuchWidget);
        if (Catalog.IsReserved(id)) throw new BridgeException(string.Format(Strings.ReservedInstall, id));
        var url = Abs(Str(entry, "package")) ?? throw new BridgeException(Strings.MarketNoSuchWidget);
        var want = (Str(entry, "sha256") ?? "").ToLowerInvariant();
        if (want.Length != 64) throw new BridgeException(Strings.MarketBadPackage);

        var bytes = await GetAsync(new Uri(url), MaxPackageBytes);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != want)
        {
            Log.Warn($"Market package {url}: SHA-256 doesn't match the list");
            throw new BridgeException(Strings.MarketBadPackage);
        }
        Directory.CreateDirectory(Paths.StagingDir);
        var file = Path.Combine(Paths.StagingDir, $"market-{Guid.NewGuid():N}.fwz");
        await File.WriteAllBytesAsync(file, bytes);
        Log.Info($"Market: downloaded {id} {Str(entry, "version")} ({bytes.Length} bytes)");
        try { app.OpenPackage(file, Strings.FromMarket); }
        finally { try { File.Delete(file); } catch (IOException) { } }
        return new { id };
    }

    /// An address in the list, made absolute; null if it isn't one we'd fetch.
    string? Abs(string? rel)
    {
        if (string.IsNullOrEmpty(rel) || indexUri == null) return null;
        return Uri.TryCreate(indexUri, rel, out var u) && Allowed(u) ? u.ToString() : null;
    }

    static async Task<byte[]> GetAsync(Uri uri, long max)
    {
        if (!Allowed(uri)) throw new BridgeException(Strings.MarketBadPackage);
        using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf)) > 0)
        {
            if (ms.Length + n > max) throw new BridgeException(Strings.MarketBadPackage);
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    static string? Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

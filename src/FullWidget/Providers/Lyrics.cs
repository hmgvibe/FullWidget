using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FullWidget.Core;

namespace FullWidget.Providers;

/// Timed lyrics for the track that is playing, looked up by title, artist and length on the
/// lyric services of the big Chinese music apps: NetEase Cloud Music, QQ Music and Kugou. The
/// app that is playing is asked first, since it is the most likely to have that exact recording.
/// Found lyrics are kept on disk; a miss is remembered for a while only, since it may have been
/// a network hiccup.
static class Lyrics
{
    public sealed record Line(long T, string Text, string? Tr);
    public sealed record Found(string Source, List<Line> Lines);
    sealed record Candidate(string Title, string Artist, long DurationMs, Func<CancellationToken, Task<Found?>> Fetch);

    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var c = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(5), PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20) })
        { Timeout = TimeSpan.FromSeconds(8) };
        // these services turn away requests that don't look like a browser
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        return c;
    }

    static readonly Dictionary<string, Task<Found?>> inFlight = new();
    static readonly Dictionary<string, long> misses = new(); // key → when it was a miss (TickCount64)
    const long MissMemoryMs = 10 * 60_000;
    static string CacheDir => Path.Combine(Paths.LocalDir, "lyrics");

    /// Lyrics for a track: { found: true, source, lines: [{ t, text, tr? }] } or { found: false }.
    public static async Task<object> FindAsync(string appId, string title, string artist, string album, long durationMs)
    {
        if (string.IsNullOrWhiteSpace(title)) return new { found = false };
        string key = Key(title, artist, durationMs);
        var hit = ReadCache(key);
        if (hit == null)
        {
            lock (misses)
                if (misses.TryGetValue(key, out var at) && Environment.TickCount64 - at < MissMemoryMs) return new { found = false };
            Task<Found?> job;
            lock (inFlight)
            {
                // two widgets asking for the same song share one lookup
                if (!inFlight.TryGetValue(key, out job!))
                    inFlight[key] = job = Task.Run(async () =>
                    {
                        try
                        {
                            var found = await SearchAsync(appId, title, artist, durationMs);
                            if (found == null) { lock (misses) misses[key] = Environment.TickCount64; }
                            else WriteCache(key, found);
                            return found;
                        }
                        finally { lock (inFlight) inFlight.Remove(key); }
                    });
            }
            hit = await job;
        }
        if (hit == null) return new { found = false };
        return new { found = true, source = hit.Source, lines = hit.Lines.Select(l => new { t = l.T, text = l.Text, tr = l.Tr }) };
    }

    // ───────────────────────── search ─────────────────────────

    static async Task<Found?> SearchAsync(string appId, string title, string artist, long durationMs)
    {
        var sources = new List<(string name, Func<string, string, long, CancellationToken, Task<List<Candidate>>> search)>
        {
            ("netease", NetEaseSearch), ("qqmusic", QQSearch), ("kugou", KugouSearch),
        };
        // the app that is playing first
        string a = appId.ToLowerInvariant();
        string first = a.Contains("cloudmusic") || a.Contains("netease") ? "netease"
            : a.Contains("qqmusic") ? "qqmusic"
            : a.Contains("kugou") ? "kugou" : "netease";
        sources.Sort((x, y) => (x.name == first ? 0 : 1) - (y.name == first ? 0 : 1));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        foreach (var (name, search) in sources)
        {
            try
            {
                var candidates = await search(title, artist, durationMs, cts.Token);
                var ranked = candidates.Select((c, i) => (c, score: Score(c, title, artist, durationMs), i))
                    .Where(x => x.score >= 70).OrderByDescending(x => x.score).ThenBy(x => x.i).Take(2);
                foreach (var (c, score, _) in ranked)
                {
                    var found = await c.Fetch(cts.Token);
                    if (found is { Lines.Count: > 0 })
                    {
                        Log.Info($"Lyrics for \"{title}\" from {name} (score {score}, {found.Lines.Count} lines)");
                        return found;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                Log.Warn($"Lyrics: {name} failed for \"{title}\": {ex.Message}");
            }
        }
        Log.Info($"Lyrics: none found for \"{title}\" / \"{artist}\"");
        return null;
    }

    static async Task<List<Candidate>> NetEaseSearch(string title, string artist, long durationMs, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://music.163.com/api/search/get/web")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["s"] = $"{title} {artist}".Trim(), ["type"] = "1", ["limit"] = "15", ["offset"] = "0" }),
        };
        req.Headers.Referrer = new Uri("https://music.163.com/");
        var j = await JsonAsync(req, ct);
        var list = new List<Candidate>();
        if (!j.TryGetProperty("result", out var r) || !r.TryGetProperty("songs", out var songs)) return list;
        foreach (var s in songs.EnumerateArray())
        {
            long id = s.GetProperty("id").GetInt64();
            list.Add(new Candidate(s.GetProperty("name").GetString() ?? "",
                string.Join("/", s.GetProperty("artists").EnumerateArray().Select(x => x.GetProperty("name").GetString())),
                s.TryGetProperty("duration", out var d) ? d.GetInt64() : 0,
                async c =>
                {
                    using var lr = new HttpRequestMessage(HttpMethod.Get, $"https://music.163.com/api/song/lyric?id={id}&lv=1&kv=1&tv=-1");
                    lr.Headers.Referrer = new Uri("https://music.163.com/");
                    var l = await JsonAsync(lr, c);
                    return Make("netease", Text(l, "lrc", "lyric"), Text(l, "tlyric", "lyric"));
                }));
        }
        return list;
    }

    static async Task<List<Candidate>> QQSearch(string title, string artist, long durationMs, CancellationToken ct)
    {
        string q = Uri.EscapeDataString($"{title} {artist}".Trim());
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?w={q}&p=1&n=15&format=json&cr=1");
        req.Headers.Referrer = new Uri("https://y.qq.com/");
        var j = await JsonAsync(req, ct);
        var list = new List<Candidate>();
        foreach (var s in j.GetProperty("data").GetProperty("song").GetProperty("list").EnumerateArray())
        {
            string mid = s.GetProperty("songmid").GetString() ?? "";
            if (mid.Length == 0) continue;
            list.Add(new Candidate(s.GetProperty("songname").GetString() ?? "",
                string.Join("/", s.GetProperty("singer").EnumerateArray().Select(x => x.GetProperty("name").GetString())),
                s.TryGetProperty("interval", out var d) ? d.GetInt64() * 1000 : 0,
                async c =>
                {
                    using var lr = new HttpRequestMessage(HttpMethod.Get, $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={mid}&format=json&nobase64=1&g_tk=5381");
                    lr.Headers.Referrer = new Uri("https://y.qq.com/");
                    var l = await JsonAsync(lr, c);
                    // the text comes HTML-escaped (&#39; and the like)
                    string Get(string k) => l.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? WebUtility.HtmlDecode(v.GetString() ?? "") : "";
                    return Make("qqmusic", Get("lyric"), Get("trans"));
                }));
        }
        return list;
    }

    static async Task<List<Candidate>> KugouSearch(string title, string artist, long durationMs, CancellationToken ct)
    {
        string q = Uri.EscapeDataString(artist.Length > 0 ? $"{artist} - {title}" : title);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword={q}&duration={durationMs}&hash=");
        var j = await JsonAsync(req, ct);
        var list = new List<Candidate>();
        if (!j.TryGetProperty("candidates", out var cs)) return list;
        foreach (var s in cs.EnumerateArray())
        {
            string id = s.GetProperty("id").ToString(), key = s.GetProperty("accesskey").GetString() ?? "";
            list.Add(new Candidate(s.GetProperty("song").GetString() ?? "", s.GetProperty("singer").GetString() ?? "",
                s.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0,
                async c =>
                {
                    using var lr = new HttpRequestMessage(HttpMethod.Get, $"https://krcs.kugou.com/download?ver=1&client=pc&id={Uri.EscapeDataString(id)}&accesskey={Uri.EscapeDataString(key)}&fmt=lrc&charset=utf8");
                    var l = await JsonAsync(lr, c);
                    string b64 = l.TryGetProperty("content", out var v) ? v.GetString() ?? "" : "";
                    return b64.Length == 0 ? null : Make("kugou", Encoding.UTF8.GetString(Convert.FromBase64String(b64)), "");
                }));
        }
        return list;
    }

    static async Task<JsonElement> JsonAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var res = await Http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        return JsonDocument.Parse(bytes).RootElement.Clone();
    }

    static string Text(JsonElement j, string a, string b) =>
        j.TryGetProperty(a, out var x) && x.ValueKind == JsonValueKind.Object && x.TryGetProperty(b, out var y) && y.ValueKind == JsonValueKind.String ? y.GetString() ?? "" : "";

    // ───────────────────────── matching ─────────────────────────

    /// 0–100: title and artist must agree; the length breaks ties and catches other versions.
    static int Score(Candidate c, string title, string artist, long durationMs)
    {
        string t1 = Norm(title), t2 = Norm(c.Title), b1 = Norm(Base(title)), b2 = Norm(Base(c.Title));
        int score = t1 == t2 ? 50 : b1 == b2 && b1.Length > 0 ? 42 : b1.Length > 1 && b2.Length > 1 && (b1.Contains(b2) || b2.Contains(b1)) ? 28 : 0;
        if (score == 0) return 0;
        if (Norm(artist).Length == 0) score += 15;
        else
        {
            var mine = Artists(artist);
            var theirs = Artists(c.Artist);
            bool shared = mine.Any(m => theirs.Any(o => o == m || (m.Length > 1 && o.Length > 1 && (o.Contains(m) || m.Contains(o)))));
            if (!shared) return 0;
            score += 35;
        }
        if (durationMs > 0 && c.DurationMs > 0)
        {
            long d = Math.Abs(durationMs - c.DurationMs);
            score += d <= 2500 ? 15 : d <= 6000 ? 8 : d <= 15000 ? 0 : -25;
        }
        else score += 5;
        return score;
    }

    static readonly Regex Brackets = new(@"\s*[\(（\[【].*?[\)）\]】]\s*|\s+-\s+.*$");
    static string Base(string s) => Brackets.Replace(s, "").Trim();

    static string Norm(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    static readonly Regex ArtistSplit = new(@"\s*(?:/|、|,|，|&|;|；|\bfeat\.?|\bft\.?|\band\b|×|\sx\s)\s*", RegexOptions.IgnoreCase);
    static List<string> Artists(string s) => ArtistSplit.Split(s).Select(Norm).Where(x => x.Length > 0).ToList();

    // ───────────────────────── LRC ─────────────────────────

    static readonly Regex Stamp = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]");
    static readonly Regex Offset = new(@"\[offset:\s*([+-]?\d+)\]", RegexOptions.IgnoreCase);
    // "作词 : 某某", "Composer：…": credits shown as lines before or after the song
    static readonly Regex Credit = new(@"^[^:：。，,！!？?]{1,14}\s*[:：]\s*\S");

    static Found? Make(string source, string lrc, string trans)
    {
        var lines = Parse(lrc);
        if (lines.Count == 0) return null;
        var tr = Parse(trans);
        if (tr.Count > 0)
        {
            var byTime = tr.Where(l => l.Text.Length > 0 && !Credit.IsMatch(l.Text)).GroupBy(l => l.T).ToDictionary(g => g.Key, g => g.First().Text);
            lines = lines.Select(l => byTime.TryGetValue(l.T, out var x) && x != l.Text ? l with { Tr = x } : l).ToList();
        }
        lines = TrimCredits(lines);
        // a track with only credits, or marked as instrumental ("纯音乐，请欣赏"), has no lyrics to show
        if (lines.Count(l => l.Text.Length > 0) < 2 || lines.Any(l => l.Text.Contains("纯音乐") && l.Text.Contains("欣赏"))) return null;
        return new Found(source, lines);
    }

    static List<Line> Parse(string lrc)
    {
        var list = new List<Line>();
        if (string.IsNullOrWhiteSpace(lrc)) return list;
        long offset = Offset.Match(lrc) is { Success: true } om ? long.Parse(om.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        foreach (var raw in lrc.Replace("\r", "").Split('\n'))
        {
            var stamps = Stamp.Matches(raw);
            if (stamps.Count == 0) continue;
            string text = Stamp.Replace(raw, "").Trim();
            foreach (Match m in stamps)
            {
                long ms = int.Parse(m.Groups[1].Value) * 60_000L + int.Parse(m.Groups[2].Value) * 1000L;
                if (m.Groups[3].Success)
                {
                    string f = m.Groups[3].Value;
                    ms += f.Length == 1 ? int.Parse(f) * 100 : f.Length == 2 ? int.Parse(f) * 10 : int.Parse(f);
                }
                list.Add(new Line(Math.Max(0, ms - offset), text, null));
            }
        }
        return list.OrderBy(l => l.T).ToList();
    }

    /// Drops the credits block at the start (and the "Title - Artist" line before it) and the
    /// one at the end; credit-like lines in the middle are left alone.
    static List<Line> TrimCredits(List<Line> lines)
    {
        bool Junk(Line l) => l.Text.Length > 0 && Credit.IsMatch(l.Text);
        int a = 0;
        // the very first line is often "Song - Singer"
        while (a < lines.Count && (lines[a].Text.Length == 0 || Junk(lines[a]) || (a < 2 && lines[a].Text.Contains(" - ")))) a++;
        int b = lines.Count;
        while (b > a && (lines[b - 1].Text.Length == 0 || Junk(lines[b - 1]))) b--;
        var kept = lines.GetRange(a, b - a);
        // the end of the last line: a blank at its end clears the screen once the song is over
        if (b < lines.Count) kept.Add(new Line(lines[b].T, "", null));
        return kept;
    }

    // ───────────────────────── cache ─────────────────────────

    static string Key(string title, string artist, long durationMs) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{Norm(title)}\n{Norm(artist)}\n{durationMs / 5000}")))[..20].ToLowerInvariant();

    static Found? ReadCache(string key)
    {
        try
        {
            var f = Path.Combine(CacheDir, key + ".json");
            return File.Exists(f) ? JsonSerializer.Deserialize<Found>(File.ReadAllText(f)) : null;
        }
        catch { return null; }
    }

    static void WriteCache(string key, Found found)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(Path.Combine(CacheDir, key + ".json"), JsonSerializer.Serialize(found));
            // keep the newest 800 songs
            var files = new DirectoryInfo(CacheDir).GetFiles("*.json");
            if (files.Length > 900)
                foreach (var old in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(800)) old.Delete();
        }
        catch (Exception ex) { Log.Warn($"Lyrics cache: {ex.Message}"); }
    }
}

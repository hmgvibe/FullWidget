using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FullWidget.Shell;

namespace FullWidget.Core;

/// Updates FullWidget itself from its GitHub releases.
///
/// The release that matches how this copy was installed is downloaded: the installer for a copy
/// the installer put there (it has unins000.exe), else the zip of the same kind (with or without
/// .NET inside). The download must match the SHA-256 that GitHub lists for it; and once releases
/// are signed, the new FullWidget.exe and .dll must carry a valid signature from the same signer
/// as this one.
///
/// The installer is run silently and opens FullWidget again when it's done. A zip is unpacked
/// next to the data, and the new FullWidget.exe from it, started with --apply-update, waits for
/// this one to quit, swaps the files (see Apply) and starts the new copy.
sealed class Updater
{
    public const string DefaultFeed = "https://api.github.com/repos/hmgvibe/FullWidget/releases/latest";
    const long MaxDownload = 300L << 20;
    static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1), Every = TimeSpan.FromHours(12);

    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var c = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"FullWidget/{App.Version}");
        return c;
    }

    readonly App app;
    readonly System.Windows.Forms.Timer timer = new();

    // what the control center shows
    string status = "idle";   // idle, checking, latest, available, downloading, applying, error
    string? error;            // a code the control center turns into words (see ui/control-center)
    Release? latest;
    double progress;
    DateTime? checkedAt;
    CancellationTokenSource? download;
    long lastNotify;

    sealed record Release(string Version, string Page, string Asset, string Url, long Size, string Sha256);

    public Updater(App app)
    {
        this.app = app;
        timer.Tick += (_, _) => { timer.Interval = (int)Every.TotalMilliseconds; _ = CheckAsync(manual: false); };
    }

    /// How this copy was installed, which decides what to download.
    public static string Kind =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe")) ? "installer"
        : File.Exists(Path.Combine(AppContext.BaseDirectory, "hostfxr.dll")) ? "full" : "zip";

    /// FULLWIDGET_UPDATE_FEED (for testing) or GitHub's latest release.
    static Uri Feed => Environment.GetEnvironmentVariable("FULLWIDGET_UPDATE_FEED") is { Length: > 0 } s && Uri.TryCreate(s, UriKind.Absolute, out var u) && Allowed(u) ? u : new Uri(DefaultFeed);

    /// https only; plain http just for a test feed on this PC.
    static bool Allowed(Uri u) => u.Scheme == "https" || (u.Scheme == "http" && u.IsLoopback);

    /// Why this copy can't replace itself, if it can't: a source tree build, or a folder it may not write.
    static string? Blocked() => blocked.Value;
    static readonly Lazy<string?> blocked = new(FindBlocked);
    static string? FindBlocked()
    {
        if (Paths.DevRoot) return "dev";
        try
        {
            var probe = Path.Combine(AppContext.BaseDirectory, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "readonly"; }
    }

    public void Start()
    {
        CleanUp();
        timer.Interval = (int)FirstCheck.TotalMilliseconds;
        timer.Enabled = app.Config.Data.UpdateCheck;
    }

    public void SetAuto(bool on)
    {
        app.Config.Data.UpdateCheck = on;
        app.Config.SaveSoon();
        if (on && !timer.Enabled) { timer.Interval = (int)FirstCheck.TotalMilliseconds; timer.Start(); }
        if (!on) timer.Stop();
    }

    public object State() => new
    {
        status,
        error,
        current = App.Version,
        latest = latest?.Version,
        page = latest?.Page,
        size = latest?.Size ?? 0,
        progress,
        checkedAt = checkedAt is { } c ? new DateTimeOffset(c).ToUnixTimeMilliseconds() : (long?)null,
        auto = app.Config.Data.UpdateCheck,
        kind = Kind,
        blocked = Blocked(),
    };

    /// A newer version is out (the tray menu offers it).
    public string? Available => latest != null && status is "available" or "downloading" or "error" && IsNewer(latest.Version) ? latest.Version : null;

    void Set(string s, string? err = null)
    {
        status = s;
        error = err;
        app.Config.Notify();
    }

    static bool IsNewer(string v) =>
        Version.TryParse(v, out var a) && Version.TryParse(App.Version, out var b) && a > b;

    // ───────────────────────── check ─────────────────────────

    public async Task CheckAsync(bool manual)
    {
        if (status is "checking" or "downloading" or "applying") return;
        Set("checking");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var req = new HttpRequestMessage(HttpMethod.Get, Feed);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await Http.SendAsync(req, cts.Token);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cts.Token));
            latest = Parse(doc.RootElement);
            checkedAt = DateTime.Now;
            if (latest == null) { Set("error", "noAsset"); return; }
            if (!IsNewer(latest.Version)) { Set("latest"); return; }
            Log.Info($"Update: {latest.Version} is out ({latest.Asset})");
            Set("available");
            // say so once per version, unless the user is already looking
            if (!manual && app.Config.Data.UpdateNotified != latest.Version)
            {
                app.Config.Data.UpdateNotified = latest.Version;
                app.Config.SaveSoon();
                app.Tray.Balloon(Strings.UpdateAvailable(latest.Version), () => app.OpenControlCenter(page: "settings"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            Log.Warn($"Update: checking failed: {ex.Message}");
            Set(manual ? "error" : "idle", manual ? "offline" : null);
        }
    }

    /// The release's version and the file for this kind of copy, with its SHA-256.
    static Release? Parse(JsonElement r)
    {
        string tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string version = tag.TrimStart('v', 'V');
        if (!Version.TryParse(version, out _)) return null;
        string page = r.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        string want = Kind switch
        {
            "installer" => $"FullWidget-{version}-setup.exe",
            "full" => $"FullWidget-{version}-win-x64-full.zip",
            _ => $"FullWidget-{version}-win-x64.zip",
        };
        if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in assets.EnumerateArray())
        {
            if (!string.Equals(a.TryGetProperty("name", out var n) ? n.GetString() : null, want, StringComparison.OrdinalIgnoreCase)) continue;
            string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            string digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
            long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
            // without a checksum there is nothing to check the download against
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Allowed(uri)) return null;
            return new Release(version, page, want, url, size, digest[7..].ToLowerInvariant());
        }
        return null;
    }

    // ───────────────────────── download and install ─────────────────────────

    public async Task InstallAsync()
    {
        if (status is "downloading" or "applying") return;
        if (latest == null || !IsNewer(latest.Version)) { await CheckAsync(manual: true); if (status != "available") return; }
        var rel = latest!;
        if (Blocked() is { } why) { Set("error", why); return; }

        download = new CancellationTokenSource();
        progress = 0;
        Set("downloading");
        string file;
        try { var token = download.Token; file = await Task.Run(() => DownloadAsync(rel, token)); } // hashing off the UI thread
        catch (OperationCanceledException) { Set("available"); return; }
        catch (InvalidDataException) { Set("error", "verify"); return; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Update: download failed: {ex.Message}");
            Set("error", "download");
            return;
        }
        finally { download?.Dispose(); download = null; }

        Set("applying");
        try
        {
            if (rel.Asset.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) RunInstaller(file);
            else await Task.Run(() => StartSwap(file, rel.Version));
            Log.Info($"Update: installing {rel.Version}, quitting");
            app.ExitThread();
        }
        catch (Exception ex)
        {
            Log.Error("Update: could not install", ex);
            Set("error", ex is UpdateException u ? u.Code : "failed");
        }
    }

    public void Cancel() => download?.Cancel();

    /// The new version's release page (or all releases): to read what's new, or to download by hand.
    public void OpenPage()
    {
        string url = latest?.Page is { } p && p.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ? p : "https://github.com/hmgvibe/FullWidget/releases/latest";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    sealed class UpdateException(string code, string message) : Exception(message) { public string Code { get; } = code; }

    static string Dir => Path.Combine(Paths.LocalDir, "update");

    /// Downloads to update\, checking the SHA-256 on the way. A file already there that checks out is used as it is.
    async Task<string> DownloadAsync(Release rel, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        string file = Path.Combine(Dir, rel.Asset), part = file + ".part";
        if (File.Exists(file) && await Sha256Async(file, ct) == rel.Sha256) return file;

        using var res = await Http.GetAsync(rel.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        long total = res.Content.Headers.ContentLength ?? rel.Size;
        if (total > MaxDownload) throw new InvalidDataException("too large");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var src = await res.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
        {
            var buf = new byte[1 << 16];
            long got = 0;
            for (int n; (n = await src.ReadAsync(buf, ct)) > 0;)
            {
                got += n;
                if (got > MaxDownload) throw new InvalidDataException("too large");
                sha.AppendData(buf, 0, n);
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                if (total > 0) progress = Math.Min(1, (double)got / total);
                long now = Environment.TickCount64;
                if (now - lastNotify > 250) { lastNotify = now; app.Post(app.Config.Notify); }
            }
        }
        string hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        if (hash != rel.Sha256)
        {
            File.Delete(part);
            Log.Warn($"Update: {rel.Asset} has SHA-256 {hash}, the release says {rel.Sha256}");
            throw new InvalidDataException("checksum");
        }
        File.Move(part, file, overwrite: true);
        progress = 1;
        return file;
    }

    static async Task<string> Sha256Async(string file, CancellationToken ct)
    {
        await using var f = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
    }

    /// The installer, silently; /relaunch=1 has it open FullWidget again afterwards.
    static void RunInstaller(string setup)
    {
        CheckSigner(setup);
        _ = Process.Start(new ProcessStartInfo(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /relaunch=1") { UseShellExecute = false })
            ?? throw new UpdateException("failed", "the installer did not start");
    }

    /// Unpacks the zip and starts the new FullWidget.exe from it, which swaps the files once this copy has quit.
    void StartSwap(string zip, string version)
    {
        string stage = Path.Combine(Dir, version);
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        ZipFile.ExtractToDirectory(zip, stage);
        // the zip holds one folder, FullWidget\
        string root = File.Exists(Path.Combine(stage, "FullWidget.exe")) ? stage
            : Directory.GetDirectories(stage).FirstOrDefault(d => File.Exists(Path.Combine(d, "FullWidget.exe")))
              ?? throw new UpdateException("verify", "no FullWidget.exe in the download");
        string exe = Path.Combine(root, "FullWidget.exe"), dll = Path.Combine(root, "FullWidget.dll");
        CheckSigner(exe);
        if (File.Exists(dll)) CheckSigner(dll);
        var v = FileVersionInfo.GetVersionInfo(File.Exists(dll) ? dll : exe).FileVersion;
        if (v == null || !Version.TryParse(v, out var fv) || fv.ToString(3) != version)
            throw new UpdateException("verify", $"the download says version {v}, not {version}");

        string args = $"\"--apply-update={AppContext.BaseDirectory.TrimEnd('\\')}\" --wait-pid={Environment.ProcessId}" + (app.DataDirArg != null ? $" \"--data-dir={app.DataDirArg}\"" : "");
        _ = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = root })
            ?? throw new UpdateException("failed", "the new version did not start");
    }

    /// Leftovers of the last update: the downloads and the old files set aside.
    void CleanUp()
    {
        try
        {
            if (Directory.Exists(Dir))
            {
                foreach (var d in Directory.GetDirectories(Dir)) TryDelete(d);
                foreach (var f in Directory.GetFiles(Dir)) if (!f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) TryDelete(f);
            }
            TryDelete(Path.Combine(AppContext.BaseDirectory, OldDir));
            var done = Path.Combine(Dir, "updated.json");
            if (File.Exists(done))
            {
                var from = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(done)).GetProperty("from").GetString();
                File.Delete(done);
                Log.Info($"Update: now {App.Version} (was {from})");
                app.Tray.Balloon(Strings.Updated(App.Version));
            }
        }
        catch (Exception ex) { Log.Warn($"Update: cleaning up: {ex.Message}"); }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ───────────────────────── swapping the files ─────────────────────────

    const string OldDir = ".update-old";

    /// Run by the new FullWidget.exe (from the unpacked zip) once the old one has quit: whatever
    /// the new version ships replaces what's in the target folder, so files dropped from widgets\,
    /// sdk\ and ui\ go too. What only the target has (data\, the "portable" mark) stays.
    /// The old files are set aside first and put back if anything fails. Then FullWidget starts.
    public static int Apply(string target, string? dataDirArg)
    {
        string source = AppContext.BaseDirectory.TrimEnd('\\');
        string old = Path.Combine(target, OldDir);
        var moved = new List<string>();
        var copied = new List<string>();
        bool ok = false;
        string from = "?";
        try { if (Version.TryParse(FileVersionInfo.GetVersionInfo(Path.Combine(target, "FullWidget.dll")).FileVersion, out var fv)) from = fv.ToString(3); }
        catch (FileNotFoundException) { }
        try
        {
            Log.Info($"Update: replacing {target} with {source}");
            if (Directory.Exists(old)) Directory.Delete(old, true);
            Directory.CreateDirectory(old);
            File.SetAttributes(old, FileAttributes.Directory | FileAttributes.Hidden);
            var entries = Directory.GetFileSystemEntries(source).Select(Path.GetFileName).OfType<string>().ToList();
            foreach (var name in entries)
            {
                string t = Path.Combine(target, name);
                if (!File.Exists(t) && !Directory.Exists(t)) continue;
                Retry(() => { if (Directory.Exists(t)) Directory.Move(t, Path.Combine(old, name)); else File.Move(t, Path.Combine(old, name)); });
                moved.Add(name);
            }
            foreach (var name in entries)
            {
                string s = Path.Combine(source, name), t = Path.Combine(target, name);
                copied.Add(name);
                if (Directory.Exists(s)) CopyDir(s, t);
                else Retry(() => File.Copy(s, t, overwrite: true));
            }
            ok = true;
            Log.Info("Update: files replaced");
        }
        catch (Exception ex)
        {
            Log.Error("Update: replacing the files failed, putting the old ones back", ex);
            foreach (var name in copied) TryDelete(Path.Combine(target, name));
            foreach (var name in moved)
            {
                try
                {
                    string o = Path.Combine(old, name), t = Path.Combine(target, name);
                    if (Directory.Exists(o)) Directory.Move(o, t); else File.Move(o, t);
                }
                catch (Exception ex2) { Log.Error($"Update: could not put back {name}", ex2); }
            }
        }

        if (ok)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, "updated.json"), JsonSerializer.Serialize(new { from }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        // the new copy (or the old one, put back) opens; it clears the leftovers
        string args = dataDirArg != null ? $"\"--data-dir={dataDirArg}\"" : "";
        try { Process.Start(new ProcessStartInfo(Path.Combine(target, "FullWidget.exe"), args) { UseShellExecute = false, WorkingDirectory = target }); }
        catch (Exception ex) { Log.Error("Update: could not start FullWidget again", ex); }
        return ok ? 0 : 1;
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) Retry(() => File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true));
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// A file can stay locked for a moment after its process quits (antivirus, the WebView2 helpers).
    static void Retry(Action a)
    {
        for (int i = 0; ; i++)
        {
            try { a(); return; }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && i < 40) { Thread.Sleep(250); }
        }
    }

    // ───────────────────────── signatures ─────────────────────────

    /// When this FullWidget.exe is signed, a new file must be signed too, validly and by the same
    /// signer. (Unsigned builds rely on the SHA-256 from GitHub alone.)
    static void CheckSigner(string file)
    {
        string mine = Environment.ProcessPath!;
        if (!Trusted(mine)) return;
        if (!Trusted(file)) throw new UpdateException("signature", $"{Path.GetFileName(file)} is not validly signed");
        if (Signer(file) != Signer(mine)) throw new UpdateException("signature", $"{Path.GetFileName(file)} is signed by {Signer(file)}, not {Signer(mine)}");
    }

#pragma warning disable SYSLIB0057 // reading the signer of a signed file, not loading a certificate
    static string? Signer(string file)
    {
        try { return System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file).Subject; }
        catch (CryptographicException) { return null; }
    }
#pragma warning restore SYSLIB0057

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WinTrustFileInfo { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustData
    {
        public int cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData;
        public int dwUIChoice, fdwRevocationChecks, dwUnionChoice; public IntPtr pFile;
        public int dwStateAction; public IntPtr hWVTStateData, pwszURLReference;
        public int dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WinTrustData data);

    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// Windows says the file's Authenticode signature is valid.
    static bool Trusted(string file)
    {
        var info = new WinTrustFileInfo { cbStruct = Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = file };
        IntPtr pInfo = Marshal.AllocHGlobal(info.cbStruct);
        try
        {
            Marshal.StructureToPtr(info, pInfo, false);
            var data = new WinTrustData
            {
                cbStruct = Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,          // WTD_UI_NONE
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = pInfo,
                dwProvFlags = 0x1000,    // WTD_CACHE_ONLY_URL_RETRIEVAL: no network
            };
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(pInfo);
            Marshal.FreeHGlobal(pInfo);
        }
    }
}

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FullWidget.Bridge;
using FullWidget.Core;
using FullWidget.Shell;
using FullWidget.Windows;
using static FullWidget.Providers.WinRt;

namespace FullWidget.Providers;

/// What's playing, from Windows' media controls (the same source as the volume flyout and the
/// lock screen: Spotify, browsers, the Media Player app…), with its play / pause / skip buttons.
///
/// It calls the Windows.Media.Control API through its COM interfaces directly, so the app doesn't
/// need the Windows SDK package (10 MB). Method numbers below follow windows.media.control.h.
/// Windows tells us about every change, so nothing is polled, and while no widget listens the
/// connection is closed.
sealed class MediaSession : IDisposable
{
    readonly App app;
    readonly HashSet<WidgetWindow> subs = new();
    readonly Handlers handlers;
    readonly System.Windows.Forms.Timer soon = new() { Interval = 60 }; // gathers a burst of change events into one update

    IntPtr manager, session;
    long tokCurrent, tokMedia, tokPlayback, tokTimeline;
    Task? starting;
    int generation;          // bumped on Stop, so a start that was still waiting gives up
    bool refreshing, dirty, wantMedia;
    string lastJson = "";

    // what we know about the current session
    bool available;
    string appId = "", appName = "";
    string? appIcon;
    string title = "", artist = "", album = "", albumArtist = "", subtitle = "";
    int trackNumber, status;
    bool canPlay, canPause, canToggle, canNext, canPrevious, canSeek;
    long start, end, position, updated;
    (string key, byte[]? bytes, string mime) cover = ("", null, "");

    public MediaSession(App app)
    {
        this.app = app;
        handlers = new Handlers(this);
        soon.Tick += (_, _) => { soon.Stop(); _ = RefreshAsync(); };
    }

    // ── for the bridge ──

    public async Task<object> GetAsync()
    {
        await EnsureAsync();
        var s = State();
        if (subs.Count == 0) Stop();
        return s;
    }

    public async Task<object> SubscribeAsync(WidgetWindow w)
    {
        subs.Add(w);
        await EnsureAsync();
        return State();
    }

    public void Unsubscribe(WidgetWindow w)
    {
        if (subs.Remove(w) && subs.Count == 0) Stop();
    }

    /// play, pause, toggle, next, previous, stop, or seek (to `positionMs` from the start).
    public async Task<bool> ControlAsync(string action, double? positionMs)
    {
        await EnsureAsync();
        IntPtr s = session;
        if (s == IntPtr.Zero) throw new BridgeException("nothing is playing");
        AddRef(s);
        try
        {
            IntPtr op;
            int hr;
            if (action == "seek")
            {
                if (positionMs is not double ms) throw new BridgeException("missing parameter 'position'");
                long ticks = start + (long)(Math.Max(0, ms) * 10_000);
                hr = CallLong(s, 24, ticks, out op); // TryChangePlaybackPositionAsync
            }
            else
            {
                int slot = action switch
                {
                    "play" => 10, "pause" => 11, "stop" => 12, "next" => 16, "previous" => 17, "toggle" => 20,
                    _ => throw new BridgeException($"unknown action '{action}' (play, pause, toggle, next, previous, stop, seek)"),
                };
                hr = Call(s, slot, out op);
            }
            if (hr < 0) return false;
            bool ok = await AwaitBool(op);
            Soon(media: false); // most apps report the change themselves; this covers the ones that are slow to
            return ok;
        }
        finally { Release(s); }
    }

    /// Timed lyrics for the current track (see Lyrics); the title, artist and duration say which
    /// track they belong to, since the song may change while they are being looked up.
    public async Task<object> LyricsAsync()
    {
        if (!available || title.Length == 0) return new { title, artist, duration = 0L, lyrics = new { found = false } };
        string t = title, a = artist.Length > 0 ? artist : albumArtist;
        long duration = Math.Max(0, (end - start) / 10_000);
        var r = await Lyrics.FindAsync(appId, t, a, album, duration);
        return new { title = t, artist, duration, lyrics = r };
    }

    /// The album art of the current track, served to widgets at /__media/cover/&lt;key&gt;.
    public (byte[] bytes, string mime)? Cover(string key) =>
        cover.bytes != null && cover.key == key ? (cover.bytes, cover.mime) : null;

    object State()
    {
        if (!available) return new { available = false };
        return new
        {
            available = true,
            app = new { id = appId, name = appName, icon = appIcon },
            title, artist, album, albumArtist, subtitle,
            track = trackNumber,
            status = status switch { 0 => "closed", 1 => "opened", 2 => "changing", 3 => "stopped", 4 => "playing", 5 => "paused", _ => "unknown" },
            playing = status == 4,
            position = Math.Max(0, (position - start) / 10_000),
            duration = Math.Max(0, (end - start) / 10_000),
            // when the position was read: it moves on by itself while playing
            updated = updated > 0 ? (updated - 116444736000000000L) / 10_000 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            cover = cover.bytes != null ? $"/__media/cover/{cover.key}" : null,
            can = new { play = canPlay, pause = canPause, toggle = canToggle, next = canNext, previous = canPrevious, seek = canSeek },
        };
    }

    void Push()
    {
        if (subs.Count == 0) return;
        var s = State();
        var json = JsonSerializer.Serialize(s, Json.Web);
        if (json == lastJson) return;
        lastJson = json;
        foreach (var w in subs.ToList()) w.Send("media", s);
    }

    // ── connecting ──

    Task EnsureAsync() => manager != IntPtr.Zero ? Task.CompletedTask : starting ??= StartAsync();

    async Task StartAsync()
    {
        int gen = generation;
        try
        {
            var iid = new Guid("2050c4ee-11a0-57de-aed7-c97c70338245"); // IGlobalSystemMediaTransportControlsSessionManagerStatics
            IntPtr cls = HString("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager");
            IntPtr statics;
            try { Marshal.ThrowExceptionForHR(RoGetActivationFactory(cls, ref iid, out statics)); }
            finally { WindowsDeleteString(cls); }
            IntPtr op;
            try { Marshal.ThrowExceptionForHR(Call(statics, 6, out op)); } // RequestAsync
            finally { Release(statics); }
            IntPtr m = await AwaitPtr(op);
            if (m == IntPtr.Zero) throw new COMException("RequestAsync gave no manager");
            if (gen != generation) { Release(m); return; } // stopped meanwhile
            manager = m;
            tokCurrent = AddHandler(manager, 8, handlers.Current); // add_CurrentSessionChanged
            AttachSession();
            await RefreshOnceAsync(media: true);
            lastJson = "";
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Warn($"Media controls unavailable: {ex.Message}");
            available = false;
        }
        finally { starting = null; }
    }

    void AttachSession()
    {
        DetachSession();
        IntPtr s;
        if (Call(manager, 6, out s) < 0 || s == IntPtr.Zero) return; // GetCurrentSession
        session = s;
        tokTimeline = AddHandler(s, 25, handlers.Timeline);  // add_TimelinePropertiesChanged
        tokPlayback = AddHandler(s, 27, handlers.Playback);  // add_PlaybackInfoChanged
        tokMedia = AddHandler(s, 29, handlers.Media);        // add_MediaPropertiesChanged
    }

    void DetachSession()
    {
        if (session == IntPtr.Zero) return;
        RemoveHandler(session, 26, tokTimeline);
        RemoveHandler(session, 28, tokPlayback);
        RemoveHandler(session, 30, tokMedia);
        Release(session);
        session = IntPtr.Zero;
    }

    void Stop()
    {
        generation++;
        soon.Stop();
        DetachSession();
        if (manager != IntPtr.Zero)
        {
            RemoveHandler(manager, 9, tokCurrent);
            Release(manager);
            manager = IntPtr.Zero;
        }
        available = false;
        appId = "";
        cover = ("", null, "");
        lastJson = "";
    }

    // Windows calls these on its own threads.
    void OnSessionChanged() { if (manager == IntPtr.Zero) return; AttachSession(); Soon(media: true); }
    void OnChanged(bool media) { if (manager != IntPtr.Zero) Soon(media); }

    void Soon(bool media)
    {
        wantMedia |= media;
        if (subs.Count == 0) return;
        soon.Stop();
        soon.Start();
    }

    // ── reading ──

    async Task RefreshAsync()
    {
        if (refreshing) { dirty = true; return; }
        refreshing = true;
        try
        {
            do
            {
                dirty = false;
                bool media = wantMedia;
                wantMedia = false;
                await RefreshOnceAsync(media);
            } while (dirty);
        }
        catch (COMException ex) { Log.Warn($"Media refresh failed: {ex.Message}"); }
        finally { refreshing = false; }
        Push();
    }

    async Task RefreshOnceAsync(bool media)
    {
        IntPtr s = session;
        if (s == IntPtr.Zero) { available = false; return; }
        AddRef(s); // the session may be swapped while we wait below
        try
        {
            available = true;
            string id = Str(s, 6); // get_SourceAppUserModelId
            if (id != appId)
            {
                appId = id;
                (appName, appIcon) = Launcher.AppByUserModelId(id);
                media = true;
            }

            IntPtr info = Ptr(s, 9); // GetPlaybackInfo
            if (info != IntPtr.Zero)
                try
                {
                    status = I32(info, 7); // get_PlaybackStatus
                    IntPtr c = Ptr(info, 6); // get_Controls
                    if (c != IntPtr.Zero)
                        try
                        {
                            canPlay = Bool(c, 6); canPause = Bool(c, 7);
                            canNext = Bool(c, 12); canPrevious = Bool(c, 13);
                            canToggle = Bool(c, 16); canSeek = Bool(c, 20);
                        }
                        finally { Release(c); }
                }
                finally { Release(info); }

            IntPtr tl = Ptr(s, 8); // GetTimelineProperties
            if (tl != IntPtr.Zero)
                try { start = I64(tl, 6); end = I64(tl, 7); position = I64(tl, 10); updated = I64(tl, 11); }
                finally { Release(tl); }

            if (!media) return;
            IntPtr op = Ptr(s, 7); // TryGetMediaPropertiesAsync
            IntPtr props = await AwaitPtr(op);
            if (props == IntPtr.Zero || s != session) { Release(props); return; }
            IntPtr thumb;
            try
            {
                title = Str(props, 6); subtitle = Str(props, 7); albumArtist = Str(props, 8);
                artist = Str(props, 9); album = Str(props, 10); trackNumber = I32(props, 11);
                thumb = Ptr(props, 15); // get_Thumbnail
            }
            finally { Release(props); }
            await LoadCoverAsync(thumb);
        }
        finally { Release(s); }
    }

    async Task LoadCoverAsync(IntPtr thumb)
    {
        string key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{appId}\n{title}\n{artist}\n{album}")))[..16].ToLowerInvariant();
        if (thumb == IntPtr.Zero) { cover = (key, null, ""); return; } // some apps send the picture a moment later
        try
        {
            if (cover.key == key && cover.bytes != null) return;
            IntPtr op = Ptr(thumb, 6); // IRandomAccessStreamReference.OpenReadAsync
            IntPtr stream = await AwaitPtr(op);
            if (stream == IntPtr.Zero) return;
            byte[]? bytes;
            try { bytes = await Task.Run(() => ReadAll(stream)); } // a blocking read; keep it off the UI thread
            finally { Release(stream); }
            cover = bytes is { Length: > 0 } ? (key, bytes, Sniff(bytes)) : (key, null, "");
        }
        finally { Release(thumb); }
    }

    static string Sniff(byte[] b) =>
        b.Length > 12 && b[0] == 0x89 && b[1] == 0x50 ? "image/png"
        : b.Length > 12 && b[0] == 'G' && b[1] == 'I' ? "image/gif"
        : b.Length > 12 && b[0] == 'R' && b[8] == 'W' && b[9] == 'E' ? "image/webp"
        : b.Length > 12 && b[0] == 'B' && b[1] == 'M' ? "image/bmp"
        : "image/jpeg";

    /// Waits for a WinRT IAsyncOperation and returns its result object (or zero). Takes over `op`.
    static async Task<IntPtr> AwaitPtr(IntPtr op)
    {
        if (op == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            if (!await Completed(op)) return IntPtr.Zero;
            return Ptr(op, 8); // GetResults
        }
        finally { Release(op); }
    }

    static async Task<bool> AwaitBool(IntPtr op)
    {
        if (op == IntPtr.Zero) return false;
        try
        {
            if (!await Completed(op)) return false;
            return Bool(op, 8);
        }
        finally { Release(op); }
    }

    /// Asks IAsyncInfo until the operation is done. These finish within milliseconds, so a few
    /// short waits are simpler than a completion callback.
    static async Task<bool> Completed(IntPtr op)
    {
        var iid = new Guid("00000036-0000-0000-C000-000000000046"); // IAsyncInfo
        if (Marshal.QueryInterface(op, in iid, out var info) < 0) return false;
        try
        {
            for (int i = 0; i < 200; i++)
            {
                int st = I32(info, 7); // get_Status: 0 started, 1 completed, 2 canceled, 3 error
                if (st != 0) return st == 1;
                await Task.Delay(i < 20 ? 5 : 25);
            }
            return false;
        }
        finally { Release(info); }
    }

    // The four TypedEventHandler<,> delegates. Their IDs are the WinRT "parameterized" IDs,
    // worked out from the type names as Windows does.
    [ComImport, Guid("228bd0ed-1fa2-5e9b-a6ec-42566173103b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICurrentSessionChangedHandler { [PreserveSig] int Invoke(IntPtr sender, IntPtr args); }
    [ComImport, Guid("0f2ce2b7-afa7-5ed0-8cb6-8c40cf9b3a5f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMediaPropertiesChangedHandler { [PreserveSig] int Invoke(IntPtr sender, IntPtr args); }
    [ComImport, Guid("2bdf1426-d41f-5896-897f-efc0b0fa7392"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPlaybackInfoChangedHandler { [PreserveSig] int Invoke(IntPtr sender, IntPtr args); }
    [ComImport, Guid("e8bf62af-fac1-5fff-9053-0bf191ae777e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITimelinePropertiesChangedHandler { [PreserveSig] int Invoke(IntPtr sender, IntPtr args); }

    [ComVisible(true)]
    sealed class Handlers : ICurrentSessionChangedHandler, IMediaPropertiesChangedHandler, IPlaybackInfoChangedHandler, ITimelinePropertiesChangedHandler
    {
        readonly MediaSession owner;
        public readonly IntPtr Current, Media, Playback, Timeline;

        public Handlers(MediaSession owner)
        {
            this.owner = owner;
            Current = Marshal.GetComInterfaceForObject(this, typeof(ICurrentSessionChangedHandler));
            Media = Marshal.GetComInterfaceForObject(this, typeof(IMediaPropertiesChangedHandler));
            Playback = Marshal.GetComInterfaceForObject(this, typeof(IPlaybackInfoChangedHandler));
            Timeline = Marshal.GetComInterfaceForObject(this, typeof(ITimelinePropertiesChangedHandler));
        }

        int ICurrentSessionChangedHandler.Invoke(IntPtr s, IntPtr a) { owner.app.Post(owner.OnSessionChanged); return 0; }
        int IMediaPropertiesChangedHandler.Invoke(IntPtr s, IntPtr a) { owner.app.Post(() => owner.OnChanged(media: true)); return 0; }
        int IPlaybackInfoChangedHandler.Invoke(IntPtr s, IntPtr a) { owner.app.Post(() => owner.OnChanged(media: false)); return 0; }
        int ITimelinePropertiesChangedHandler.Invoke(IntPtr s, IntPtr a) { owner.app.Post(() => owner.OnChanged(media: false)); return 0; }

        public void Dispose()
        {
            foreach (var p in new[] { Current, Media, Playback, Timeline }) Marshal.Release(p);
        }
    }

    public void Dispose()
    {
        subs.Clear();
        Stop();
        soon.Dispose();
        handlers.Dispose();
    }
}

/// Calls into WinRT objects by method number, for MediaSession.
static unsafe class WinRt
{
    public static void** V(IntPtr o) => *(void***)o;
    public static void AddRef(IntPtr o) { if (o != IntPtr.Zero) Marshal.AddRef(o); }
    public static void Release(IntPtr o) { if (o != IntPtr.Zero) Marshal.Release(o); }

    public static IntPtr Ptr(IntPtr o, int slot)
    {
        IntPtr r = IntPtr.Zero;
        return ((delegate* unmanaged<IntPtr, IntPtr*, int>)V(o)[slot])(o, &r) < 0 ? IntPtr.Zero : r;
    }
    public static int I32(IntPtr o, int slot) { int r = 0; ((delegate* unmanaged<IntPtr, int*, int>)V(o)[slot])(o, &r); return r; }
    public static long I64(IntPtr o, int slot) { long r = 0; ((delegate* unmanaged<IntPtr, long*, int>)V(o)[slot])(o, &r); return r; }
    public static bool Bool(IntPtr o, int slot) { byte r = 0; ((delegate* unmanaged<IntPtr, byte*, int>)V(o)[slot])(o, &r); return r != 0; }

    public static string Str(IntPtr o, int slot)
    {
        IntPtr h = IntPtr.Zero;
        if (((delegate* unmanaged<IntPtr, IntPtr*, int>)V(o)[slot])(o, &h) < 0 || h == IntPtr.Zero) return "";
        try { uint n; char* p = WindowsGetStringRawBuffer(h, &n); return new string(p, 0, (int)n).Trim(); }
        finally { WindowsDeleteString(h); }
    }

    public static IntPtr HString(string s)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(s, s.Length, out var h));
        return h;
    }

    public static long AddHandler(IntPtr o, int slot, IntPtr handler)
    {
        long token = 0;
        return ((delegate* unmanaged<IntPtr, IntPtr, long*, int>)V(o)[slot])(o, handler, &token) < 0 ? 0 : token;
    }
    public static void RemoveHandler(IntPtr o, int slot, long token)
    {
        if (token != 0) ((delegate* unmanaged<IntPtr, long, int>)V(o)[slot])(o, token);
    }

    public static int Call(IntPtr o, int slot, out IntPtr result)
    {
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged<IntPtr, IntPtr*, int>)V(o)[slot])(o, &r);
        result = r;
        return hr;
    }

    public static int CallLong(IntPtr o, int slot, long arg, out IntPtr result)
    {
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged<IntPtr, long, IntPtr*, int>)V(o)[slot])(o, arg, &r);
        result = r;
        return hr;
    }

    public static byte[]? ReadAll(IntPtr randomAccessStream)
    {
        var iid = new Guid("0000000c-0000-0000-C000-000000000046"); // IStream
        if (CreateStreamOverRandomAccessStream(randomAccessStream, ref iid, out var st) < 0) return null;
        try
        {
            using var ms = new MemoryStream();
            var buf = new byte[64 * 1024];
            fixed (byte* b = buf)
                while (true)
                {
                    uint n;
                    int hr = ((delegate* unmanaged<IntPtr, byte*, uint, uint*, int>)V(st)[3])(st, b, (uint)buf.Length, &n); // Read
                    if (hr < 0 || n == 0) break;
                    ms.Write(buf, 0, (int)n);
                    if (ms.Length > 6 * 1024 * 1024) return null;
                }
            return ms.ToArray();
        }
        finally { Release(st); }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)] public static extern int WindowsCreateString(string s, int length, out IntPtr hstring);
    [DllImport("combase.dll")] public static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll")] public static extern char* WindowsGetStringRawBuffer(IntPtr hstring, uint* length);
    [DllImport("combase.dll")] public static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("shcore.dll")] public static extern int CreateStreamOverRandomAccessStream(IntPtr randomAccessStream, ref Guid iid, out IntPtr stream);

}
